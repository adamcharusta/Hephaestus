using Hephaestus.Core.Readiness;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Trust;

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

    public static Signal Labeled(string id, long? actor = 29, string? login = "alice", string label = "hephaestus", int issue = 7, long repository = RepositoryId, string? title = "Add a login page", string? body = "Details.") =>
        new(Signal.GitHubSource, id, SignalKind.Labeled, repository, issue, actor, login, label, null, null, new DateTimeOffset(2026, 11, 27, 11, 0, 0, TimeSpan.Zero), title, body);

    public static Signal Comment(string id, long? actor = 29, string? login = "alice", string? body = "Use Postgres.", int issue = 7, long commentId = 500, long repository = RepositoryId, DateTimeOffset? at = null) =>
        new(Signal.GitHubSource, id, SignalKind.CommentCreated, repository, issue, actor, login, null, commentId, actor, at ?? new DateTimeOffset(2026, 11, 27, 13, 0, 0, TimeSpan.Zero), CommentBody: body);

    public static Signal Unlabeled(string id, long? actor = 1001, string? login = "bob", string label = "hephaestus:queued", int issue = 7, long repository = RepositoryId) =>
        new(Signal.GitHubSource, id, SignalKind.Unlabeled, repository, issue, actor, login, label, null, null, new DateTimeOffset(2026, 11, 27, 11, 5, 0, TimeSpan.Zero));

    public Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<long?> GetGitHubAppIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Alias>> ListAliasesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<AgentSetting>> ListAgentSettingsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<AgentCapacity>> ListAgentCapacitiesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<InstallationLimits> GetLimitsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<string>> ListProtectedPathsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<string, string>> ListSettingsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<string, string>>(Settings);
}
