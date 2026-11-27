using System.Collections.Concurrent;
using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using Hephaestus.Core.Output;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Secrets;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Workspaces;
using Hephaestus.Workspaces.Egress;

namespace Hephaestus.Workspaces;

/// <summary>A step container the runtime has started and must see to its end (AD-18).</summary>
/// <param name="Deadline">When the container is stopped if it still runs; recorded on the step run before the container was asked for.</param>
public sealed record WatchedStep(Guid TaskId, Guid StepRunId, string ContainerId, DateTimeOffset Deadline);

/// <summary>What the runtime does with a step container after it has started it. The loop never takes part (AD-18).</summary>
public interface IStepWatcher
{
    /// <summary>Starts seeing to the step's end and returns at once. A step run that is already watched is not watched twice.</summary>
    void Watch(WatchedStep step);
}

/// <summary>
/// Sees every step container to its end (AD-18, FR-19, FR-53): it follows the container's output through the masker into <see cref="IOutputStore"/>,
/// stops the container when its deadline passes, which nothing else does, appends exactly one step-exited Signal, and removes the container and the
/// egress proxy, network, and volumes of the step run. The order of the end is: output complete, Signal appended, container removed. A crash after the Signal leaves a labelled
/// container that recovery finds (Story 3.17); a crash before it leaves the container, which is watched again from the start of its log.
/// </summary>
/// <remarks>
/// What fails while a step is watched is reported and, for what the step's end depends on, tried again at an interval until it works or the host stops: the looks at the container,
/// the stop at its deadline, the start and completion of the output file, and the append of the Signal. Output that cannot be read to its end and a removal that fails are reported once
/// and not tried again. The output is recorded beside the look at the container, so a store that fails does not hold back the deadline; while Docker itself cannot be reached nothing can be
/// stopped, and the deadline is acted on at the first look after it answers. Disposing the watcher stops watching and leaves the containers as they are.
/// </remarks>
public sealed class StepWatcher : IStepWatcher, IAsyncDisposable
{
    private readonly IDockerClient _docker;
    private readonly WorkspaceRuntimeOptions _options;
    private readonly ISignalInbox _inbox;
    private readonly IOutputStore _output;
    private readonly SecretMasker _masker;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action<string, Exception?>? _problem;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<Guid, Task> _watching = new();
    private int _disposed;

    /// <param name="problem">Told of what fails while a step is watched, in a sentence that names the step run and holds no output of the step; it is tried again after each.</param>
    /// <param name="delay">Waits for an interval; tests give one that moves their clock.</param>
    public StepWatcher(
        IDockerClient docker,
        WorkspaceRuntimeOptions options,
        ISignalInbox inbox,
        IOutputStore output,
        SecretMasker masker,
        TimeProvider time,
        Action<string, Exception?>? problem = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _docker = docker;
        _options = options;
        _inbox = inbox;
        _output = output;
        _masker = masker;
        _time = time;
        _problem = problem;
        _delay = delay ?? ((interval, token) => Task.Delay(interval, time, token));
    }

    public void Watch(WatchedStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        CancellationToken stopping;
        try
        {
            stopping = _stopping.Token;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (stopping.IsCancellationRequested)
        {
            return;
        }

        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_watching.TryAdd(step.StepRunId, finished.Task))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await WatchAsync(step, stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                // The host is stopping. The container stays, labelled, for recovery.
            }
            catch (Exception exception)
            {
                Report($"Step run {step.StepRunId:D} is no longer watched.", exception);
            }
            finally
            {
                _watching.TryRemove(step.StepRunId, out _);
                finished.SetResult();
            }
        });
    }

    /// <summary>Completes when every step watched now has been seen to its end; tests wait on it.</summary>
    internal Task WhenIdleAsync() => Task.WhenAll(_watching.Values);

    /// <summary>Stops watching and waits until every watch has stopped. The containers are left as they are.</summary>
    public async ValueTask DisposeAsync()
    {
        // A container of the host's services can be asked to dispose the one watcher more than once, through the two types it is registered as.
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _stopping.CancelAsync();
        await Task.WhenAll(_watching.Values);
        _stopping.Dispose();
    }

    private async Task WatchAsync(WatchedStep step, CancellationToken stopping)
    {
        // The output is recorded beside the look at the container, so that a store that fails does not hold back the deadline.
        using var abandon = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var recording = RecordOutputAsync(step, abandon.Token, stopping);
        try
        {
            var end = await WaitForEndAsync(step, stopping);

            // The output follows the container to its end; it is given a while to arrive, and what has arrived by then is what is kept.
            try
            {
                await recording.WaitAsync(_options.OutputDrainLimit, _time, stopping);
            }
            catch (TimeoutException)
            {
                Report($"The output of step run {step.StepRunId:D} did not end after its container did; what arrived is kept.", null);
                await abandon.CancelAsync();
                await recording;
            }

            // The proxy's log is complete once the step has ended, so what it refused is read now, before anything is removed, and reported with the end.
            var refused = await ReadRefusedHostsAsync(step, stopping);
            await RetryAsync(
                $"The end of step run {step.StepRunId:D} could not be reported.",
                token => _inbox.AppendAsync([Signal.StepExited(step.StepRunId, end.ExitCode, end.Cause, _time.GetUtcNow(), refused)], token),
                stopping);
            await RemoveAsync(step, stopping);
        }
        finally
        {
            // Whatever ended the watch, the output is no longer written to when it does, and its file is closed.
            await abandon.CancelAsync();
            try
            {
                await recording;
            }
            catch (OperationCanceledException)
            {
                // The host is stopping.
            }
            catch (Exception exception)
            {
                Report($"The output file of step run {step.StepRunId:D} could not be closed.", exception);
            }
        }
    }

    /// <summary>
    /// Writes the container's output to the store: starts the file, follows the log from its start until the container ends, and completes the file. A failure to read the
    /// output is reported and ends it with what came, because the step's end does not depend on it; the store is waited for until it answers or <paramref name="abandoned"/> is cancelled,
    /// when no file is left to complete. Completing the file is waited for until the host stops.
    /// </summary>
    private async Task RecordOutputAsync(WatchedStep step, CancellationToken abandoned, CancellationToken stopping)
    {
        IOutputWriter writer;
        try
        {
            writer = await RetryAsync($"The output file of step run {step.StepRunId:D} could not be started.", token => _output.BeginAsync(new OutputRun(step.TaskId, step.StepRunId, step.ContainerId), token), abandoned);
        }
        catch (OperationCanceledException) when (abandoned.IsCancellationRequested && !stopping.IsCancellationRequested)
        {
            Report($"The output file of step run {step.StepRunId:D} was never started, so its output is lost.", null);
            return;
        }

        await using (writer)
        {
            var sink = new MaskedOutputSink(writer, _masker);
            await CopyOutputAsync(step, sink, abandoned);
            await RetryAsync($"The output file of step run {step.StepRunId:D} could not be completed.", async token =>
            {
                await sink.CompleteAsync(token);
                return await writer.CompleteAsync(token);
            }, stopping);
        }
    }

    /// <summary>Follows the container's output from its start until the container ends. It reports what goes wrong and never fails: the step's end does not depend on it.</summary>
    private async Task CopyOutputAsync(WatchedStep step, MaskedOutputSink sink, CancellationToken cancellationToken)
    {
        try
        {
            using var logs = await _docker.Containers.GetContainerLogsAsync(
                step.ContainerId,
                new ContainerLogsParameters { ShowStdout = true, ShowStderr = true, Follow = true },
                cancellationToken);
            // A read that does not honor cancellation is ended by closing the stream.
            using var closing = cancellationToken.Register(logs.Dispose);
            await logs.CopyOutputToAsync(Stream.Null, sink, sink, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopped by the host, or given up after the drain limit.
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            Report($"The output of step run {step.StepRunId:D} could not be read to its end.", exception);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // A read ended by closing the stream, as above.
        }
    }

    /// <summary>
    /// Looks at the container until it has ended, and stops it once, when its deadline has passed (FR-19). Docker's wait call is not used: a request
    /// of the client is limited to minutes and a step may run for hours.
    /// </summary>
    private async Task<StepEnd> WaitForEndAsync(WatchedStep step, CancellationToken stopping)
    {
        var timedOut = false;
        var stopped = false;
        while (true)
        {
            var state = await RetryAsync($"Step run {step.StepRunId:D} could not be looked at.", token => InspectAsync(step.ContainerId, token), stopping);
            if (state is null)
            {
                return new StepEnd(StepExitCause.Lost, null);
            }

            if (state.Status is "exited" or "dead")
            {
                var cause = timedOut ? StepExitCause.Timeout : state.OOMKilled ? StepExitCause.OutOfMemory : StepExitCause.Exited;
                return new StepEnd(cause, unchecked((int)state.ExitCode));
            }

            var remaining = step.Deadline - _time.GetUtcNow();
            if (remaining <= TimeSpan.Zero && !stopped)
            {
                // Only a stop that ended a running container is a timeout: one that finds it stopped already, by itself, is not.
                timedOut = await RetryAsync($"Step run {step.StepRunId:D} could not be stopped at its deadline.", token => StopAsync(step.ContainerId, token), stopping);
                stopped = true;
                continue;
            }

            await _delay(remaining > TimeSpan.Zero && remaining < _options.StepPollInterval ? remaining : _options.StepPollInterval, stopping);
        }
    }

    /// <summary>
    /// The hosts the step run's egress proxy refused, from its log (AD-7, AD-18); none when the proxy is already gone. A log that cannot be read is reported once, and the step ends with
    /// no host recorded: its end does not depend on it.
    /// </summary>
    private async Task<IReadOnlyList<string>> ReadRefusedHostsAsync(WatchedStep step, CancellationToken stopping)
    {
        try
        {
            return EgressRefusals.Parse(await ContainerLogs.ReadAsync(_docker, WorkspaceLabels.EgressProxy(step.StepRunId), stopping));
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }
        catch (Exception exception) when (!stopping.IsCancellationRequested)
        {
            Report($"The hosts that the egress proxy of step run {step.StepRunId:D} refused could not be read; none is recorded.", exception);
            return [];
        }
    }

    /// <summary>The container's state, or null when there is no such container.</summary>
    private async Task<StepState?> InspectAsync(string container, CancellationToken cancellationToken)
    {
        try
        {
            var state = (await _docker.Containers.InspectContainerAsync(container, cancellationToken)).State;
            return new StepState(state?.Status, state?.ExitCode ?? 0, state?.OOMKilled ?? false);
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Stops the container, and says whether this stop ended it; one that has stopped already, or is gone, is not an error, and not a timeout.</summary>
    private async Task<bool> StopAsync(string container, CancellationToken cancellationToken)
    {
        try
        {
            await _docker.Containers.StopContainerAsync(container, new ContainerStopParameters { WaitBeforeKillSeconds = _options.StopGraceSeconds }, cancellationToken);
            return true;
        }
        catch (DockerApiException exception) when (exception.StatusCode is HttpStatusCode.NotModified or HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>
    /// Removes the container, the step run's egress proxy and network, and its context and Methodology volumes. The Signal has been appended by now, so a failure here is reported and
    /// left to the removal of what a crash leaves behind, which selects on the installation label (Story 3.17).
    /// </summary>
    private async Task RemoveAsync(WatchedStep step, CancellationToken stopping)
    {
        await RemoveOneAsync(step, () => _docker.Containers.RemoveContainerAsync(step.ContainerId, new ContainerRemoveParameters { Force = true, RemoveVolumes = true }, stopping), stopping);
        await RemoveOneAsync(step, () => _docker.Containers.RemoveContainerAsync(WorkspaceLabels.EgressProxy(step.StepRunId), new ContainerRemoveParameters { Force = true, RemoveVolumes = true }, stopping), stopping);
        await RemoveOneAsync(step, () => _docker.Networks.DeleteNetworkAsync(WorkspaceLabels.StepNetwork(step.StepRunId), stopping), stopping);
        await RemoveOneAsync(step, () => _docker.Volumes.RemoveAsync(WorkspaceLabels.ContextVolume(step.StepRunId), false, stopping), stopping);
        await RemoveOneAsync(step, () => _docker.Volumes.RemoveAsync(WorkspaceLabels.MethodologyVolume(step.StepRunId), false, stopping), stopping);
    }

    /// <summary>Each removal is tried alone, so that one that fails does not keep the others from being tried.</summary>
    private async Task RemoveOneAsync(WatchedStep step, Func<Task> remove, CancellationToken stopping)
    {
        try
        {
            await IgnoreMissingAsync(remove);
        }
        catch (Exception exception) when (IsTransient(exception) && !stopping.IsCancellationRequested)
        {
            Report($"A container, network, or volume of step run {step.StepRunId:D} could not be removed; it is left for cleanup.", exception);
        }
    }

    private static async Task IgnoreMissingAsync(Func<Task> remove)
    {
        try
        {
            await remove();
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // Not there, which is what removal is for.
        }
    }

    /// <summary>Runs an operation until it succeeds; a failure is reported and tried again after the retry interval, until the host stops.</summary>
    private async Task<T> RetryAsync<T>(string what, Func<CancellationToken, Task<T>> operation, CancellationToken stopping)
    {
        while (true)
        {
            try
            {
                return await operation(stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Report(what, exception);
                await _delay(_options.RetryInterval, stopping);
            }
        }
    }

    /// <summary>Tells whoever listens; a listener that fails must not end the watch.</summary>
    private void Report(string problem, Exception? exception)
    {
        try
        {
            _problem?.Invoke(problem, exception);
        }
        catch (Exception)
        {
            // Nothing is left to tell.
        }
    }

    private static bool IsTransient(Exception exception) =>
        exception is DockerApiException or HttpRequestException or IOException or TimeoutException or TaskCanceledException;

    private sealed record StepState(string? Status, long ExitCode, bool OOMKilled);

    private sealed record StepEnd(StepExitCause Cause, int? ExitCode);
}
