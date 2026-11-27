using Hephaestus.Core.Effects;
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
/// <param name="EffectsEnded">How many effects on GitHub ended in a result this tick.</param>
/// <param name="EffectsRetried">How many attempts at an effect failed, or met GitHub's wait, and were scheduled again.</param>
/// <param name="Problems">The effects that could not be completed, for the host to log as errors with the Task's id.</param>
public sealed record TickReport(
    int Applied,
    int Rejected,
    int TasksCreated,
    Exception? Failure,
    IReadOnlyList<string> Rejections,
    int EffectsEnded = 0,
    int EffectsRetried = 0,
    IReadOnlyList<EffectProblem>? Problems = null);

/// <summary>An effect that ended without being made: the far side stayed unreachable until the bound, or refused for good. The Task waits for the Owner.</summary>
/// <param name="Result">Gave up or refused.</param>
/// <param name="Reason">One of <see cref="EffectReasons"/>.</param>
/// <param name="MovedToWaitingForOwner">Whether the Task moved to <c>waiting-for-owner</c> because of it.</param>
public sealed record EffectProblem(Guid TaskId, string Key, EffectResult Result, string Reason, bool MovedToWaitingForOwner);

/// <summary>
/// The orchestrator loop (AD-1): the only writer of Task state, and, through <see cref="IUnitOfWork"/>, the only code that depends on
/// it. Each tick takes the Signals nobody has applied, in inbox order, and applies each in one transaction. Intent is read from
/// actor-attributed Signals and never from the labels an issue has now (AD-2), and one admission function decides trust by numeric
/// id (AD-15). Startup recovery is the first tick: a Signal appended and not yet applied when the host stopped is still pending, and
/// so is an effect whose intent was journaled and whose result was not (AD-5).
/// </summary>
public sealed partial class OrchestratorLoop(
    ISignalInbox inbox,
    IReadStore reads,
    IUnitOfWork work,
    ICodeHost host,
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
        EffectsRun effects = new();

        // The Tasks that already have an effect to show their state, so that a flood of hand-made changes to one issue is answered once, not
        // once for each. Read when a state label change first needs it, and kept up to date as this tick journals intents.
        HashSet<Guid>? showing = null;

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
                        var existing = await FindTaskAsync(stored.Signal, cancellationToken);
                        var covered = false;
                        if (existing is not null && IsStateLabelChange(stored.Signal))
                        {
                            showing ??= await TasksShowingTheirStateAsync(cancellationToken);
                            covered = showing.Contains(existing.Id);
                        }

                        var application = Decide(stored, context, existing, covered);
                        if (await work.ApplyAsync(application, cancellationToken) is false)
                        {
                            continue;
                        }

                        if (application.NewEffects is { Count: > 0 } && (application.NewTask?.Task.Id ?? application.TaskId) is { } taskId)
                        {
                            showing?.Add(taskId);
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

        // The effects the Signals caused, and any that was left unfinished, are made only after the transactions that journaled them. A
        // Signal that fails to apply does not hold them back: they are not its business.
        try
        {
            await RunEffectsAsync(effects, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        failure ??= effects.Failure;

        if (failure is null)
        {
            heartbeat.Beat();
        }

        return new TickReport(applied, rejections.Count, created, failure, rejections, effects.Ended, effects.Retried, effects.Problems);
    }

    private static string RejectionLine(StoredSignal stored, string? reason) =>
        $"Signal {stored.Sequence} of actor {stored.Signal.ActorId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"} rejected: {reason}";

    private async Task<TaskRecord?> FindTaskAsync(Signal signal, CancellationToken cancellationToken) =>
        IsMarkerLabeled(signal) || IsStateLabelChange(signal)
            ? await reads.FindTaskAsync(signal.RepositoryId, signal.IssueNumber, cancellationToken)
            : null;

    private static bool IsMarkerLabeled(Signal signal) => signal.Kind == SignalKind.Labeled && Labels.IsMarker(signal.Label);

    /// <summary>A state label that was applied or removed, by anyone: what the Task shows on its issue is a projection that is put back when it is changed by hand (AD-2).</summary>
    private static bool IsStateLabelChange(Signal signal) => signal.Kind is SignalKind.Labeled or SignalKind.Unlabeled && Labels.TryParseState(signal.Label, out _);

    private async Task<Context> ReadContextAsync(CancellationToken cancellationToken)
    {
        var trusted = (await reads.ListTrustedUsersAsync(cancellationToken)).Select(user => user.UserId).ToHashSet();
        var projects = (await reads.ListProjectsAsync(includeRemoved: false, cancellationToken)).Select(project => project.RepositoryId).ToHashSet();
        var slug = await reads.GetGitHubAppSlugAsync(cancellationToken);
        return new Context(trusted, projects, slug is null ? null : GitHubLogin.BotOf(slug));
    }

    /// <summary>The Tasks that have an effect, journaled and not ended, that shows the state they have now.</summary>
    private async Task<HashSet<Guid>> TasksShowingTheirStateAsync(CancellationToken cancellationToken) =>
        [.. (await reads.ListUnfinishedEffectsAsync(cancellationToken))
            .Where(pending => pending.Effect.Kind == EffectKind.Label && pending.Task.State.Tag() == pending.Effect.Detail)
            .Select(pending => pending.Task.Id)];

    private SignalApplication Decide(StoredSignal stored, Context context, TaskRecord? existing, bool covered)
    {
        var signal = stored.Signal;

        if (IsStateLabelChange(signal))
        {
            return Reproject(stored, context, existing, covered);
        }

        // What the later stories of the epic act on (withdrawal, close, comments, commands) is applied with no change for now: the
        // Signal is consumed, and nothing is inferred from it.
        if (!IsMarkerLabeled(signal))
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
        return new SignalApplication(stored.Sequence, SignalStatus.Applied, TaskId: task.Id, NewTask: new NewTask(task, snapshot), NewEffects: [ShowState(task, stored.Sequence)]);
    }

    /// <summary>The effect that makes the issue show the Task's state, journaled in the same transaction as the Task is written (FR-11, AD-5).</summary>
    private static NewEffect ShowState(TaskRecord task, long signalSequence) =>
        new(task.Id, EffectKeys.SignalCause(signalSequence), EffectKind.Label, task.State.Tag());

    /// <summary>
    /// A state label was applied or removed. Whoever did it, the issue is made to show the Task's state again (AD-2): the label is a one-way
    /// projection of the database, and the effect reads the labels the issue has before it changes any. Hephaestus's own change is not
    /// answered, because it is what was wanted. A label of an issue that is no Task is nobody's business. When an effect that shows the state
    /// is waiting already (<paramref name="covered"/>), it reads the labels as they are when it is made, so it answers this change too, and
    /// nothing is journaled for it: whoever can label an issue cannot make Hephaestus ask GitHub once for every change.
    /// </summary>
    private static SignalApplication Reproject(StoredSignal stored, Context context, TaskRecord? existing, bool covered)
    {
        if (existing is null || covered || Admission.Decide(stored.Signal.ActorId, stored.Signal.ActorLogin, context.TrustedUserIds, context.OwnLogin) == Admitted.OwnIdentity)
        {
            return new SignalApplication(stored.Sequence, SignalStatus.Applied, TaskId: existing?.Id);
        }

        return new SignalApplication(stored.Sequence, SignalStatus.Applied, TaskId: existing.Id, NewEffects: [ShowState(existing, stored.Sequence)]);
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
