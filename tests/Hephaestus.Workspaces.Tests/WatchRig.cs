using System.Text;
using Hephaestus.Core.Output;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Secrets;
using Hephaestus.Core.Signals;
using NSubstitute;

namespace Hephaestus.Workspaces.Tests;

/// <summary>An inbox in memory that keeps what is appended, refuses a duplicate as the real one does, and can be made to fail.</summary>
internal sealed class InMemoryInbox(List<string> calls) : ISignalInbox
{
    public List<Signal> Appended { get; } = [];

    public Func<int, Exception?>? Fails { get; set; }

    private int _attempts;

    public Task<long> AppendOneAsync(Hephaestus.Core.Signals.Signal signal, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<int> AppendAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken)
    {
        if (Fails?.Invoke(++_attempts) is { } failure)
        {
            return Task.FromException<int>(failure);
        }

        var added = 0;
        foreach (var signal in signals)
        {
            if (!Appended.Any(a => a.Source == signal.Source && a.SourceEventId == signal.SourceEventId))
            {
                Appended.Add(signal);
                calls.Add("append signal");
                added++;
            }
        }

        return Task.FromResult(added);
    }

    public Task<IReadOnlyList<StoredSignal>> ReadAsync(long afterSequence, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<StoredSignal>> ReadPendingAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>An output store in memory: one buffer for each step run, and whether it was completed.</summary>
internal sealed class InMemoryOutput(List<string> calls) : IOutputStore
{
    public Dictionary<Guid, Buffer> Files { get; } = [];

    public Func<int, Exception?>? BeginFails { get; set; }

    public Func<int, Exception?>? CompleteFails { get; set; }

    private int _begins;

    private int _completes;

    public Task<IOutputWriter> BeginAsync(OutputRun run, CancellationToken cancellationToken)
    {
        if (BeginFails?.Invoke(++_begins) is { } failure)
        {
            return Task.FromException<IOutputWriter>(failure);
        }

        var buffer = new Buffer(run);
        Files[run.StepRunId] = buffer;
        calls.Add("begin output");
        return Task.FromResult<IOutputWriter>(new Writer(buffer, calls, () => CompleteFails?.Invoke(++_completes)));
    }

    public Task<StoredOutput?> FindAsync(Guid stepRunId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Stream?> OpenReadAsync(Guid stepRunId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public sealed class Buffer(OutputRun run)
    {
        public OutputRun Run { get; } = run;

        public MemoryStream Bytes { get; } = new();

        public bool Complete { get; set; }

        public bool Disposed { get; set; }

        public string Text => Encoding.UTF8.GetString(Bytes.ToArray());
    }

    private sealed class Writer(Buffer buffer, List<string> calls, Func<Exception?> completeFails) : IOutputWriter
    {
        public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            buffer.Bytes.Write(bytes.Span);
            return ValueTask.CompletedTask;
        }

        public Task<StoredOutput> CompleteAsync(CancellationToken cancellationToken)
        {
            if (completeFails() is { } failure)
            {
                return Task.FromException<StoredOutput>(failure);
            }

            buffer.Complete = true;
            calls.Add("complete output");
            return Task.FromResult(new StoredOutput(buffer.Run.TaskId, buffer.Run.StepRunId, buffer.Run.ContainerId, buffer.Bytes.Length, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        }

        public ValueTask DisposeAsync()
        {
            buffer.Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// A step watcher over a Docker Engine in memory, an inbox and an output store in memory, and a clock the test moves: waiting for an interval moves
/// the clock by it and yields, so a step that runs for hours is watched in a moment. Everything the watcher does is in <see cref="DockerDouble.Calls"/>, in order.
/// </summary>
internal sealed class WatchRig : IAsyncDisposable
{
    public static readonly Guid TaskId = Guid.Parse("018f4b6e-0000-7000-8000-000000000001");
    public static readonly Guid StepRunId = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");
    public static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    public const string Container = "container-1";

    public WatchRig(string[]? secrets = null, WorkspaceRuntimeOptions? options = null)
    {
        Docker = new DockerDouble();
        Inbox = new InMemoryInbox(Docker.Calls);
        Output = new InMemoryOutput(Docker.Calls);
        Time = new MovingTime(Noon);
        var store = Substitute.For<ISecretStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(secrets ?? []);
        Masker = new SecretMasker(store);
        Masker.RebuildAsync(CancellationToken.None).GetAwaiter().GetResult();
        Options = options ?? new WorkspaceRuntimeOptions("test-installation", new FakeVendor().Lock, Hephaestus.Core.AgentBinaries.AgentArchitecture.X64);
        Watcher = new StepWatcher(
            Docker.Client,
            Options,
            Inbox,
            Output,
            Masker,
            Time,
            (problem, exception) => Problems.Add(exception is null ? problem : $"{problem} ({exception.GetType().Name})"),
            async (interval, token) =>
            {
                token.ThrowIfCancellationRequested();
                Delays.Add(interval);
                Time.Advance(interval);
                await Task.Yield();
            });
    }

    public DockerDouble Docker { get; }

    public InMemoryInbox Inbox { get; }

    public InMemoryOutput Output { get; }

    public MovingTime Time { get; }

    public SecretMasker Masker { get; }

    public WorkspaceRuntimeOptions Options { get; }

    public StepWatcher Watcher { get; }

    public List<string> Problems { get; } = [];

    public List<TimeSpan> Delays { get; } = [];

    public WatchedStep Step(TimeSpan? deadlineIn = null) => new(TaskId, StepRunId, Container, Noon + (deadlineIn ?? TimeSpan.FromHours(1)));

    /// <summary>Watches the step and waits until it has been seen to its end.</summary>
    public async Task WatchToTheEndAsync(WatchedStep? step = null)
    {
        Watcher.Watch(step ?? Step());
        await Watcher.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => Watcher.DisposeAsync();
}
