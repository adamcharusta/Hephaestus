using Hephaestus.Core.GitHub;
using Hephaestus.Core.Identity;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Trust;

namespace Hephaestus.Core.Loop;

/// <summary>What one tick of the loop did, for the host to log.</summary>
/// <param name="Applied">How many Signals were applied.</param>
/// <param name="Rejected">How many Signals were recorded as rejected, with the actor's id and the reason.</param>
/// <param name="TasksCreated">How many Tasks the tick created.</param>
/// <param name="Failure">What stopped the tick, when something did; the Signal that failed is tried again on the next tick.</param>
/// <param name="Rejections">One line for each rejected Signal: its sequence, the actor's numeric id, and the reason.</param>
public sealed record TickReport(int Applied, int Rejected, int TasksCreated, Exception? Failure, IReadOnlyList<string> Rejections);

/// <summary>
/// The orchestrator loop (AD-1): the only writer of Task state, and, through <see cref="IUnitOfWork"/>, the only code that depends on
/// it. Each tick takes the Signals nobody has applied, in inbox order, and applies each in one transaction. Intent is read from
/// actor-attributed Signals and never from the labels an issue has now (AD-2), and one admission function decides trust by numeric
/// id (AD-15). Startup recovery is the first tick: a Signal appended and not yet applied when the host stopped is still pending.
/// </summary>
public sealed class OrchestratorLoop(
    ISignalInbox inbox,
    IReadStore reads,
    IUnitOfWork work,
    TimeProvider time,
    LoopHeartbeat heartbeat,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    /// <summary>How long the loop waits when no Signal is pending.</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    /// <summary>How many Signals are read at once; installation configuration is read again before each batch.</summary>
    public const int BatchSize = 50;

    /// <summary>How many Signals one tick applies at most, so that the loop beats and the host can stop between ticks.</summary>
    public const int MaximumSignalsPerTick = 500;

    /// <summary>How many times applying one Signal may fail before it can be set aside, so that it cannot hold back the Signals behind it.</summary>
    public const int MaximumApplyFailures = 3;

    /// <summary>
    /// How long a Signal must have been failing before it is set aside. A database that is unavailable for a short while must not cost a
    /// Trusted User their opt-in, so the failures have to span this time as well as number <see cref="MaximumApplyFailures"/>.
    /// </summary>
    public static readonly TimeSpan ApplyFailureWindow = TimeSpan.FromMinutes(5);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((interval, token) => Task.Delay(interval, time, token));
    private readonly Dictionary<long, (int Count, DateTimeOffset First)> _failures = [];

    /// <summary>Ticks until <paramref name="cancellationToken"/> is cancelled; a tick that met a failure is reported with it and tried again after one interval.</summary>
    public async Task RunAsync(Action<TickReport> report, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TickReport tick;
            try
            {
                tick = await TickAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                report(tick);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // What the host does with a report must not end the loop: nothing would apply a Signal after it.
            }

            try
            {
                // A tick that stopped at the cap has more waiting; one that failed, or found nothing, waits.
                if (tick.Failure is not null || tick.Applied + tick.Rejected < MaximumSignalsPerTick)
                {
                    await _delay(TickInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Applies the pending Signals, in inbox order, up to <see cref="MaximumSignalsPerTick"/>. The loop beats as each Signal is applied and
    /// when the tick ends without a failure, so a loop that fails on every tick is not reported alive.
    /// </summary>
    public async Task<TickReport> TickAsync(CancellationToken cancellationToken)
    {
        var applied = 0;
        var created = 0;
        var rejections = new List<string>();
        var handled = 0;
        Exception? failure = null;

        try
        {
            while (handled < MaximumSignalsPerTick && failure is null)
            {
                var pending = await inbox.ReadPendingAsync(Math.Min(BatchSize, MaximumSignalsPerTick - handled), cancellationToken);
                if (pending.Count == 0)
                {
                    break;
                }

                var context = await ReadContextAsync(cancellationToken);
                foreach (var stored in pending)
                {
                    try
                    {
                        var application = Decide(stored, context, await FindTaskAsync(stored.Signal, cancellationToken));
                        if (await work.ApplyAsync(application, cancellationToken) is false)
                        {
                            continue;
                        }

                        _failures.Remove(stored.Sequence);
                        handled++;
                        if (application.Status == SignalStatus.Rejected)
                        {
                            rejections.Add(RejectionLine(stored, application.Reason));
                        }
                        else
                        {
                            applied++;
                        }

                        created += application.NewTask is null ? 0 : 1;
                        heartbeat.Beat();
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                        if (await SetAsideAfterRepeatedFailureAsync(stored, cancellationToken))
                        {
                            handled++;
                            rejections.Add(RejectionLine(stored, SignalRejections.ApplyFailed));
                        }

                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (failure is null)
        {
            heartbeat.Beat();
        }

        return new TickReport(applied, rejections.Count, created, failure, rejections);
    }

    private static string RejectionLine(StoredSignal stored, string? reason) =>
        $"Signal {stored.Sequence} of actor {stored.Signal.ActorId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"} rejected: {reason}";

    private async Task<TaskRecord?> FindTaskAsync(Signal signal, CancellationToken cancellationToken) =>
        signal.Kind == SignalKind.Labeled && Labels.IsMarker(signal.Label)
            ? await reads.FindTaskAsync(signal.RepositoryId, signal.IssueNumber, cancellationToken)
            : null;

    private async Task<Context> ReadContextAsync(CancellationToken cancellationToken)
    {
        var trusted = (await reads.ListTrustedUsersAsync(cancellationToken)).Select(user => user.UserId).ToHashSet();
        var projects = (await reads.ListProjectsAsync(includeRemoved: false, cancellationToken)).Select(project => project.RepositoryId).ToHashSet();
        var slug = await reads.GetGitHubAppSlugAsync(cancellationToken);
        return new Context(trusted, projects, slug is null ? null : GitHubLogin.BotOf(slug));
    }

    private SignalApplication Decide(StoredSignal stored, Context context, TaskRecord? existing)
    {
        var signal = stored.Signal;

        // What the later stories of the epic act on (withdrawal, close, state labels, comments, commands) is applied with no change
        // for now: the Signal is consumed, and nothing is inferred from it.
        if (signal.Kind != SignalKind.Labeled || !Labels.IsMarker(signal.Label))
        {
            return new SignalApplication(stored.Sequence, SignalStatus.Applied);
        }

        if (!context.Projects.Contains(signal.RepositoryId))
        {
            return Reject(stored, SignalRejections.ProjectNotRegistered);
        }

        switch (Admission.Decide(signal.ActorId, signal.ActorLogin, context.TrustedUserIds, context.OwnLogin))
        {
            case Admitted.OwnIdentity:
                return Reject(stored, SignalRejections.OwnIdentity);
            case Admitted.NoActor:
                return Reject(stored, SignalRejections.ActorUnknown);
            case Admitted.NotTrusted:
                return Reject(stored, SignalRejections.ActorNotTrusted);
        }

        if (existing is not null)
        {
            // A Task is one Project and one issue for life (AD-2); opting in again belongs to Story 2.3.
            return new SignalApplication(stored.Sequence, SignalStatus.Applied, TaskId: existing.Id);
        }

        if (signal.IssueTitle is not { } title)
        {
            return Reject(stored, SignalRejections.SnapshotMissing);
        }

        var body = signal.IssueBody ?? string.Empty;
        var now = time.GetUtcNow();
        var task = new TaskRecord(EntityId.New(time), signal.RepositoryId, signal.IssueNumber, TaskState.Queued, BranchName.For(signal.IssueNumber, title), now, now);
        var snapshot = new IssueSnapshot(title, body, IssueContent.Hash(title, body), stored.Sequence, now);
        return new SignalApplication(stored.Sequence, SignalStatus.Applied, TaskId: task.Id, NewTask: new NewTask(task, snapshot));
    }

    private static SignalApplication Reject(StoredSignal stored, string reason) => new(stored.Sequence, SignalStatus.Rejected, reason);

    /// <summary>Counts a failure of the Signal and, once it has failed often enough and for long enough, rejects it. Returns whether it was rejected.</summary>
    private async Task<bool> SetAsideAfterRepeatedFailureAsync(StoredSignal stored, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var (count, first) = _failures.TryGetValue(stored.Sequence, out var earlier) ? (earlier.Count + 1, earlier.First) : (1, now);
        _failures[stored.Sequence] = (count, first);
        if (count < MaximumApplyFailures || now - first < ApplyFailureWindow)
        {
            return false;
        }

        try
        {
            if (await work.ApplyAsync(Reject(stored, SignalRejections.ApplyFailed), cancellationToken))
            {
                _failures.Remove(stored.Sequence);
                return true;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The database is what is failing; the next tick tries this Signal again.
        }

        return false;
    }

    private sealed record Context(IReadOnlySet<long> TrustedUserIds, IReadOnlySet<long> Projects, string? OwnLogin);
}
