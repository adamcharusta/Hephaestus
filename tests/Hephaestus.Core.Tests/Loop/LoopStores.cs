using Hephaestus.Core.Aliases;
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

    public string? AppSlug { get; set; }

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

        return Task.FromResult(true);
    }

    public static Signal Labeled(string id, long? actor = 29, string? login = "alice", string label = "hephaestus", int issue = 7, long repository = RepositoryId, string? title = "Add a login page", string? body = "Details.") =>
        new(Signal.GitHubSource, id, SignalKind.Labeled, repository, issue, actor, login, label, null, null, new DateTimeOffset(2026, 11, 27, 11, 0, 0, TimeSpan.Zero), title, body);

    public Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<long?> GetGitHubAppIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Alias>> ListAliasesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<InstallationLimits> GetLimitsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<string>> ListProtectedPathsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<string, string>> ListSettingsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
