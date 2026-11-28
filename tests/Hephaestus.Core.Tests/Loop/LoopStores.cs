using Hephaestus.Core.Readiness;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.Failures;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Runs;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Trust;
using Hephaestus.Core.Windows;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>
/// The three ports the loop uses, in memory with the rules of the real ones: the inbox gives the pending Signals in order, the read
/// store holds what the unit of work wrote, and a Signal is applied only while it is pending.
/// </summary>
internal sealed class LoopStores : IReadStore, ISignalInbox, IUnitOfWork
{
    public const long RepositoryId = 101;

    public List<StoredSignal> Signals { get; } = [];

    public List<TrustedUser> TrustedUsers { get; } = [new(29, "alice", DateTimeOffset.UnixEpoch)];

    public List<Project> Projects { get; } = [new(RepositoryId, "acme/web", "main", DateTimeOffset.UnixEpoch, null)];

    public List<TaskRecord> Tasks { get; } = [];

    public Dictionary<Guid, List<IssueSnapshot>> Snapshots { get; } = [];

    /// <summary>The comments of Trusted Users the loop admitted, in the order it admitted them.</summary>
    public List<AdmittedComment> AdmittedComments { get; } = [];

    public string? AppSlug { get; set; }

    /// <summary>The windows of the Projects, in the order they were opened.</summary>
    public List<Window> Windows { get; } = [];

    /// <summary>The step runs of the Tasks, in the order they were planned.</summary>
    public List<StepRun> StepRuns { get; } = [];

    /// <summary>What each Task started with.</summary>
    public Dictionary<Guid, TaskSnapshot> TaskSnapshots { get; } = [];

    /// <summary>The Aliases the loop resolves an Agent from.</summary>
    public List<Alias> Aliases { get; } = [];

    public List<AgentSetting> AgentSettings { get; } = [];

    public List<AgentCapacity> AgentCapacities { get; } = [];

    public InstallationLimits Limits { get; set; } = InstallationLimits.None;

    /// <summary>GitHub, as far as the loop asks it: the labels of the issues.</summary>
    public FakeIssues Github { get; } = new();

    public Dictionary<string, string> Settings { get; } = [];

    /// <summary>The notifier the loop calls when an Escalation is raised.</summary>
    public RecordingNotifier Notifier { get; } = new();

    /// <summary>The Escalations recorded on Tasks, in the order they were raised.</summary>
    public List<Escalation> Escalations { get; } = [];

    /// <summary>The effect journal, in the order it was written, with the rules of the real one: an effect has one intent and at most one result.</summary>
    public List<JournalEntry> Journal { get; } = [];

    public List<EffectRecording> Recordings { get; } = [];

    /// <summary>Whether recording the end of an effect fails, and with what; for a test of a loop that must try again.</summary>
    public Exception? FailToRecord { get; set; }

    /// <summary>The Signals whose application fails, by sequence, with how many more times each is to fail.</summary>
    public Dictionary<long, int> Failures { get; } = [];

    public List<SignalApplication> Applications { get; } = [];

    /// <summary>When set, reading the pending Signals fails with it, after <see cref="FailToReadAfter"/> reads have succeeded.</summary>
    public Exception? FailToRead { get; set; }

    public int FailToReadAfter { get; set; }

    public int Reads { get; private set; }

    /// <summary>Runs just before a Signal is applied, so that a test can let something else get there first.</summary>
    public Action<long>? BeforeApply { get; set; }

    public Task<string?> GetGitHubAppSlugAsync(CancellationToken cancellationToken) => Task.FromResult(AppSlug);

    public Task<IReadOnlyList<TrustedUser>> ListTrustedUsersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TrustedUser>>([.. TrustedUsers]);

    public Task<IReadOnlyList<Project>> ListProjectsAsync(bool includeRemoved, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Project>>([.. Projects.Where(p => includeRemoved || p.IsActive)]);

    public Task<TaskRecord?> FindTaskAsync(long repositoryId, int issueNumber, CancellationToken cancellationToken) =>
        Task.FromResult(Tasks.FirstOrDefault(t => t.RepositoryId == repositoryId && t.IssueNumber == issueNumber));

    public Task<IssueSnapshot?> FindIssueSnapshotAsync(Guid taskId, CancellationToken cancellationToken) =>
        Task.FromResult(Snapshots.TryGetValue(taskId, out var list) ? list[^1] : null);

    public Task<int> AppendAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken)
    {
        foreach (var signal in signals)
        {
            Signals.Add(new StoredSignal(Signals.Count + 1, signal));
        }

        return Task.FromResult(signals.Count);
    }

    public Task<IReadOnlyList<StoredSignal>> ReadAsync(long afterSequence, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StoredSignal>>([.. Signals.Where(s => s.Sequence > afterSequence).Take(limit)]);

    public Task<IReadOnlyList<StoredSignal>> ReadPendingAsync(int limit, CancellationToken cancellationToken)
    {
        Reads++;
        return FailToRead is not null && Reads > FailToReadAfter
            ? throw FailToRead
            : Task.FromResult<IReadOnlyList<StoredSignal>>([.. Signals.Where(s => s.Status == SignalStatus.Pending).Take(limit)]);
    }

    public Task<bool> ApplyAsync(SignalApplication application, CancellationToken cancellationToken)
    {
        if (Failures.TryGetValue(application.Sequence, out var remaining) && remaining > 0)
        {
            Failures[application.Sequence] = remaining - 1;
            throw new InvalidOperationException("The database is not there.");
        }

        BeforeApply?.Invoke(application.Sequence);
        var index = Signals.FindIndex(s => s.Sequence == application.Sequence);
        if (index < 0 || Signals[index].Status != SignalStatus.Pending)
        {
            return Task.FromResult(false);
        }

        Applications.Add(application);
        Signals[index] = Signals[index] with { Status = application.Status, Reason = application.Reason, TaskId = application.NewTask?.Task.Id ?? application.TaskId };
        if (application.NewTask is { } created)
        {
            Tasks.Add(created.Task);
            Snapshots[created.Task.Id] = [created.Snapshot];
        }

        if (application.MoveTaskTo is { } to)
        {
            var moved = Tasks.FindIndex(t => t.Id == application.TaskId);
            Tasks[moved] = Tasks[moved] with { State = to, UpdatedAt = TimeProvider.GetUtcNow() };
        }

        if (application.EndEscalation is { } outcome)
        {
            var open = Escalations.FindIndex(e => e.TaskId == application.TaskId && e.ClosedAt is null);
            if (open >= 0)
            {
                Escalations[open] = Escalations[open] with { ClosedAt = TimeProvider.GetUtcNow(), Outcome = outcome };
            }
        }

        if (application.AdmitComment is { } admitted)
        {
            AdmittedComments.Add(admitted);
        }

        if (application.OpenWindow is { } opened)
        {
            if (Windows.Any(w => w.RepositoryId == opened.RepositoryId && w.IsOpen))
            {
                throw new InvalidOperationException("A Project has at most one open window.");
            }

            Windows.Add(opened);
        }

        if (application.CloseWindow is { } closing)
        {
            Close(closing);
        }

        if (application.StepRun is { } stepRun)
        {
            Change(stepRun);
        }

        AppendIntents(application.NewEffects);
        return Task.FromResult(true);
    }

    public Task<bool> EscalateAsync(EscalationPlan plan, CancellationToken cancellationToken) => Task.FromResult(Write(plan));

    /// <summary>The rule of the real unit of work: the move is made only while the Task is in the state the plan was made for.</summary>
    private bool Write(EscalationPlan plan)
    {
        var index = Tasks.FindIndex(t => t.Id == plan.Escalation.TaskId && t.State == plan.From);
        if (index < 0)
        {
            return false;
        }

        Tasks[index] = Tasks[index] with { State = plan.To, UpdatedAt = TimeProvider.GetUtcNow() };
        Escalations.Add(plan.Escalation);
        AppendIntents(plan.Effects);
        return true;
    }

    /// <summary>Runs when an effect is recorded, before anything is written; a test makes it throw at the recording it chooses.</summary>
    public Action? OnRecord { get; set; }

    public Task<bool> RecordEffectAsync(EffectRecording recording, CancellationToken cancellationToken)
    {
        OnRecord?.Invoke();
        if (FailToRecord is not null)
        {
            throw FailToRecord;
        }

        if (Journal.Any(e => e.Effect.Key == recording.Effect.Key && e.Entry == JournalEntryKind.Result))
        {
            return Task.FromResult(false);
        }

        Recordings.Add(recording);
        var now = TimeProvider.GetUtcNow();
        Journal.Add(new JournalEntry(
            Journal.Count + 1,
            recording.Result is null ? JournalEntryKind.Retry : JournalEntryKind.Result,
            recording.Effect,
            recording.Result,
            recording.Reason,
            recording.Failures,
            recording.NextAttemptAt,
            now));
        if (recording.Escalation is { } plan)
        {
            Write(plan);
        }

        // The rule of the real unit of work: the move is made only while the Task is in the state it leaves, and its intents with it.
        if (recording.Move is { } move && Tasks.FindIndex(t => t.Id == recording.Effect.TaskId && t.State == move.From) is var index and >= 0)
        {
            Tasks[index] = Tasks[index] with { State = move.To, UpdatedAt = TimeProvider.GetUtcNow() };
            AppendIntents(move.Effects);
        }

        return Task.FromResult(true);
    }

    private readonly HashSet<(long? RepositoryId, int? IssueNumber, string NoticeKey)> Notices = [];

    /// <summary>The clock the journal's lines are stamped with.</summary>
    public TimeProvider TimeProvider { get; set; } = new MovingTime();

    private void AppendIntents(IReadOnlyList<NewEffect>? effects)
    {
        foreach (var effect in effects ?? [])
        {
            // The rule of the real journal: what is said on an issue about one notice is said once.
            if (effect.NoticeKey is { } notice && !Notices.Add((effect.RepositoryId, effect.IssueNumber, notice)))
            {
                continue;
            }

            var scope = effect.Scope;
            var ordinal = Journal.Count(e => e.Entry == JournalEntryKind.Intent && EffectKeys.Scope(e.Effect.TaskId, e.Effect.RepositoryId) == scope && e.Effect.Cause == effect.Cause && e.Effect.Kind == effect.Kind);
            var key = EffectKeys.For(scope, effect.Cause, effect.Kind, ordinal);
            Journal.Add(new JournalEntry(Journal.Count + 1, JournalEntryKind.Intent, new Effect(key, effect.TaskId, effect.Cause, effect.Kind, effect.Detail, ordinal, effect.RepositoryId, effect.IssueNumber, effect.Payload), null, null, 0, null, TimeProvider.GetUtcNow()));
        }
    }

    public Task<IReadOnlyList<PendingEffect>> ListUnfinishedEffectsAsync(CancellationToken cancellationToken)
    {
        var unfinished = Journal
            .Where(e => e.Entry == JournalEntryKind.Intent && !Journal.Any(r => r.Effect.Key == e.Effect.Key && r.Entry == JournalEntryKind.Result))
            .Select(e =>
            {
                var last = Journal.LastOrDefault(r => r.Effect.Key == e.Effect.Key && r.Entry == JournalEntryKind.Retry);
                return new PendingEffect(e.Effect, e.Effect.TaskId is { } id ? Tasks.Single(t => t.Id == id) : null, last?.Failures ?? 0, last?.NextAttemptAt);
            });
        return Task.FromResult<IReadOnlyList<PendingEffect>>([.. unfinished]);
    }

    public Task<IReadOnlyList<AdmittedComment>> ListAdmittedCommentsAsync(Guid taskId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AdmittedComment>>([.. AdmittedComments.Where(c => c.TaskId == taskId)]);

    public Task<Escalation?> FindEscalationAsync(Guid escalationId, CancellationToken cancellationToken) =>
        Task.FromResult(Escalations.FirstOrDefault(e => e.Id == escalationId));

    public Task<IReadOnlyList<Escalation>> ListEscalationsAsync(Guid taskId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Escalation>>([.. Escalations.Where(e => e.TaskId == taskId)]);

    public Task<IReadOnlyList<JournalEntry>> ListJournalAsync(Guid? taskId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<JournalEntry>>([.. Journal.Where(e => taskId is null || e.Effect.TaskId == taskId)]);

    public Task<TaskRecord?> FindTaskAsync(Guid taskId, CancellationToken cancellationToken) => Task.FromResult(Tasks.FirstOrDefault(t => t.Id == taskId));

    public Task<IReadOnlyList<TaskRecord>> ListTasksAsync(TaskState state, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskRecord>>([.. Tasks.Where(t => t.State == state).OrderBy(t => t.IssueNumber).ThenBy(t => t.RepositoryId)]);

    public Task<TaskSnapshot?> FindTaskSnapshotAsync(Guid taskId, CancellationToken cancellationToken) => Task.FromResult(TaskSnapshots.GetValueOrDefault(taskId));

    public Task<IReadOnlyList<StepRun>> ListStepRunsAsync(Guid taskId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StepRun>>([.. StepRuns.Where(s => s.TaskId == taskId).OrderBy(s => s.Sequence)]);

    public Task<StepRun?> FindStepRunAsync(Guid stepRunId, CancellationToken cancellationToken) => Task.FromResult(StepRuns.FirstOrDefault(s => s.Id == stepRunId));

    public Task<IReadOnlyList<Window>> ListWindowsAsync(bool openOnly, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Window>>([.. Windows.Where(w => !openOnly || w.IsOpen)]);

    public Task<long> AppendOneAsync(Signal signal, CancellationToken cancellationToken)
    {
        var existing = Signals.FirstOrDefault(s => s.Signal.Source == signal.Source && s.Signal.SourceEventId == signal.SourceEventId);
        if (existing is not null)
        {
            return Task.FromResult(existing.Sequence);
        }

        Signals.Add(new StoredSignal(Signals.Count + 1, signal));
        return Task.FromResult((long)Signals.Count);
    }

    /// <summary>The rule of the real unit of work: the Task moves to Running only from Queued, and only while no Task is Running; its snapshot is kept, its first step run planned, and the intents journaled.</summary>
    public Task<bool> DispatchAsync(TaskDispatch dispatch, CancellationToken cancellationToken)
    {
        var index = Tasks.FindIndex(t => t.Id == dispatch.TaskId && t.State == TaskState.Queued);
        if (index < 0 || Tasks.Any(t => t.State == TaskState.Running))
        {
            return Task.FromResult(false);
        }

        Tasks[index] = Tasks[index] with { State = TaskState.Running, UpdatedAt = TimeProvider.GetUtcNow() };
        if (dispatch.Snapshot is { } snapshot)
        {
            TaskSnapshots[snapshot.TaskId] = snapshot;
        }

        Plan(dispatch.FirstStep);
        AppendIntents(dispatch.Effects);
        return Task.FromResult(true);
    }

    public Task<bool> PlanStepRunAsync(NewStepRun run, CancellationToken cancellationToken)
    {
        if (!Tasks.Any(t => t.Id == run.TaskId && t.State == TaskState.Running) || StepRuns.Any(s => s.Id == run.Id))
        {
            return Task.FromResult(false);
        }

        Plan(run);
        return Task.FromResult(true);
    }

    private void Plan(NewStepRun run) => StepRuns.Add(new StepRun(
        run.Id,
        run.TaskId,
        StepRuns.Count(s => s.TaskId == run.TaskId) + 1,
        run.Kind,
        run.Round,
        StepRunStatus.Planned,
        run.Deadline,
        run.PlannedAt,
        run.TipBefore,
        Agent: run.Agent,
        Model: run.Model,
        Effort: run.Effort,
        Methodology: run.Methodology,
        Template: run.Template,
        CommandIndex: run.CommandIndex));

    public Task<bool> ChangeStepRunAsync(StepRunChange change, CancellationToken cancellationToken) => Task.FromResult(Change(change));

    /// <summary>The rule of the real unit of work: a run moves only from a status the change may be made from, the Task moves only from the state it leaves, and the intents are journaled with them.</summary>
    private bool Change(StepRunChange change)
    {
        var index = StepRuns.FindIndex(s => s.Id == change.StepRunId && change.To.MayMoveToFrom().Contains(s.Status));
        if (index < 0)
        {
            return false;
        }

        var run = StepRuns[index] with { Status = change.To };
        run = change.To switch
        {
            StepRunStatus.Started => run with { StartedAt = TimeProvider.GetUtcNow() },
            StepRunStatus.Ended => run with
            {
                EndedAt = change.End!.At,
                ExitCode = change.End.ExitCode,
                Cause = change.End.Cause,
                Failure = change.End.Failure?.Tag(),
                Outcome = change.End.Outcome,
                Result = change.End.Result,
            },
            StepRunStatus.Checkpointed => run with { TipAfter = change.TipAfter },
            _ => run with { Failure = change.Failure?.Tag() ?? run.Failure },
        };
        StepRuns[index] = run;
        if (change.Move is { } move && Tasks.FindIndex(t => t.Id == run.TaskId && t.State == move.From) is var moved and >= 0)
        {
            Tasks[moved] = Tasks[moved] with { State = move.To, UpdatedAt = TimeProvider.GetUtcNow() };
            AppendIntents(move.Effects);
        }

        AppendIntents(change.Effects);
        return true;
    }

    public Task<bool> CloseWindowAsync(WindowClosing closing, CancellationToken cancellationToken) => Task.FromResult(Close(closing));

    private bool Close(WindowClosing closing)
    {
        var index = Windows.FindIndex(w => w.Id == closing.WindowId && w.IsOpen);
        if (index < 0)
        {
            return false;
        }

        Windows[index] = Windows[index] with { ClosedAt = TimeProvider.GetUtcNow(), End = closing.End };
        return true;
    }

    public static Signal Labeled(string id, long? actor = 29, string? login = "alice", string label = "hephaestus", int issue = 7, long repository = RepositoryId, string? title = "Add a login page", string? body = "Details.") =>
        new(Signal.GitHubSource, id, SignalKind.Labeled, repository, issue, actor, login, label, null, null, new DateTimeOffset(2026, 11, 27, 11, 0, 0, TimeSpan.Zero), title, body);

    public static Signal Comment(string id, long? actor = 29, string? login = "alice", string? body = "Use Postgres.", int issue = 7, long commentId = 500, long repository = RepositoryId, DateTimeOffset? at = null) =>
        new(Signal.GitHubSource, id, SignalKind.CommentCreated, repository, issue, actor, login, null, commentId, actor, at ?? new DateTimeOffset(2026, 11, 27, 13, 0, 0, TimeSpan.Zero), CommentBody: body);

    public static Signal Unlabeled(string id, long? actor = 1001, string? login = "bob", string label = "hephaestus:queued", int issue = 7, long repository = RepositoryId) =>
        new(Signal.GitHubSource, id, SignalKind.Unlabeled, repository, issue, actor, login, label, null, null, new DateTimeOffset(2026, 11, 27, 11, 5, 0, TimeSpan.Zero));

    public Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<long?> GetGitHubAppIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Alias>> ListAliasesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Alias>>([.. Aliases]);

    public Task<IReadOnlyList<AgentSetting>> ListAgentSettingsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AgentSetting>>([.. AgentSettings]);

    public Task<IReadOnlyList<AgentCapacity>> ListAgentCapacitiesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AgentCapacity>>([.. AgentCapacities]);

    public Task<InstallationLimits> GetLimitsAsync(CancellationToken cancellationToken) => Task.FromResult(Limits);

    public Task<IReadOnlyList<string>> ListProtectedPathsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<string, string>> ListSettingsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<string, string>>(Settings);
}
