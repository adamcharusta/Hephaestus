using Hephaestus.Core.Aliases;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Trust;

namespace Hephaestus.GitHub.Tests;

/// <summary>The three ports the gateway keeps its credential in, in memory.</summary>
internal sealed class FakeStores : ISecretStore, IReadStore, IInstallationConfiguration
{
    public Dictionary<string, string> Secrets { get; } = [];

    public long? AppId { get; set; }

    /// <summary>When set, recording the App id fails with it.</summary>
    public Exception? FailToRecordAppId { get; set; }

    /// <summary>When set, storing a secret fails with it.</summary>
    public Exception? FailToStoreSecret { get; set; }

    public event Func<Task>? Changed
    {
        add { }
        remove { }
    }

    public Task SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        if (FailToStoreSecret is not null)
        {
            // A store that wrote its file and then failed on the name leaves the value behind.
            Secrets[name] = value;
            throw FailToStoreSecret;
        }

        Secrets[name] = value;
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string name, CancellationToken cancellationToken) => Task.FromResult(Secrets.Remove(name));

    public Task<string?> ReadAsync(string name, CancellationToken cancellationToken) => Task.FromResult(Secrets.GetValueOrDefault(name));

    public Task<IReadOnlyList<string>> ReadAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([.. Secrets.Values]);

    public Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([.. Secrets.Keys.Order()]);

    public Task<IReadOnlyList<Alias>> ListAliasesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<InstallationLimits> GetLimitsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<string>> ListProtectedPathsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Dictionary<string, string> Settings { get; } = [];

    /// <summary>When set, reading the settings fails with it.</summary>
    public Exception? FailToReadSettings { get; set; }

    public Task<IReadOnlyDictionary<string, string>> ListSettingsAsync(CancellationToken cancellationToken) =>
        FailToReadSettings is null ? Task.FromResult<IReadOnlyDictionary<string, string>>(Settings) : throw FailToReadSettings;

    public Task<AliasWrite> AddAliasAsync(Alias alias, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Alias?> RemoveAliasAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<InstallationLimits> SetLimitsAsync(LimitsChange change, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> AddProtectedPathAsync(string glob, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> RemoveProtectedPathAsync(string glob, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetSettingAsync(string name, string value, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<long?> GetGitHubAppIdAsync(CancellationToken cancellationToken) => Task.FromResult(AppId);

    public string? AppSlug { get; set; }

    /// <summary>How many times the App's id and slug were recorded.</summary>
    public int AppIdWrites { get; private set; }

    public Task<string?> GetGitHubAppSlugAsync(CancellationToken cancellationToken) => Task.FromResult(AppSlug);

    public Task<Hephaestus.Core.Tasks.TaskRecord?> FindTaskAsync(long repositoryId, int issueNumber, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Hephaestus.Core.Tasks.IssueSnapshot?> FindIssueSnapshotAsync(Guid taskId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Hephaestus.Core.Effects.PendingEffect>> ListUnfinishedEffectsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Hephaestus.Core.Effects.JournalEntry>> ListJournalAsync(Guid? taskId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetGitHubAppIdAsync(long appId, string slug, CancellationToken cancellationToken)
    {
        if (FailToRecordAppId is not null)
        {
            throw FailToRecordAppId;
        }

        AppIdWrites++;
        AppId = appId;
        AppSlug = slug;
        return Task.CompletedTask;
    }

    public List<TrustedUser> TrustedUsers { get; } = [];

    public Task<IReadOnlyList<TrustedUser>> ListTrustedUsersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TrustedUser>>([.. TrustedUsers]);

    public Task<TrustedUserWrite> AddTrustedUserAsync(long userId, string login, CancellationToken cancellationToken)
    {
        var existing = TrustedUsers.FindIndex(u => u.UserId == userId);
        var user = new TrustedUser(userId, login, existing >= 0 ? TrustedUsers[existing].AddedAt : DateTimeOffset.UnixEpoch);
        if (existing >= 0)
        {
            TrustedUsers[existing] = user;
        }
        else
        {
            TrustedUsers.Add(user);
        }

        return Task.FromResult(new TrustedUserWrite(user, existing < 0));
    }

    public Task<bool> RemoveTrustedUserAsync(long userId, CancellationToken cancellationToken) => Task.FromResult(TrustedUsers.RemoveAll(u => u.UserId == userId) > 0);

    public List<Project> Projects { get; } = [];

    public Task<IReadOnlyList<Project>> ListProjectsAsync(bool includeRemoved, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Project>>([.. Projects.Where(p => includeRemoved || p.IsActive)]);

    public Task<ProjectWrite> RegisterProjectAsync(long repositoryId, string fullName, string baseBranch, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Project?> RemoveProjectAsync(long repositoryId, CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>The Signal inbox in memory, with the same rule as the real one: a source and a source event id are appended once.</summary>
internal sealed class FakeInbox : Hephaestus.Core.Ports.ISignalInbox
{
    public List<Hephaestus.Core.Signals.StoredSignal> Stored { get; } = [];

    /// <summary>The Signals in inbox order.</summary>
    public IEnumerable<Hephaestus.Core.Signals.Signal> Signals => Stored.Select(s => s.Signal);

    /// <summary>The number of appends that were asked for, whether or not anything in them was new.</summary>
    public int Appends { get; private set; }

    /// <summary>When set, an append fails with it, as a crash between the append and the cursor would not be seen: nothing is stored.</summary>
    public Exception? Fail { get; set; }

    public Task<int> AppendAsync(IReadOnlyList<Hephaestus.Core.Signals.Signal> signals, CancellationToken cancellationToken)
    {
        Appends++;
        if (Fail is not null)
        {
            throw Fail;
        }

        var appended = 0;
        foreach (var signal in signals)
        {
            if (!Stored.Any(s => s.Signal.Source == signal.Source && s.Signal.SourceEventId == signal.SourceEventId))
            {
                Stored.Add(new Hephaestus.Core.Signals.StoredSignal(Stored.Count + 1, signal));
                appended++;
            }
        }

        return Task.FromResult(appended);
    }

    public Task<IReadOnlyList<Hephaestus.Core.Signals.StoredSignal>> ReadAsync(long afterSequence, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Hephaestus.Core.Signals.StoredSignal>>([.. Stored.Where(s => s.Sequence > afterSequence).Take(limit)]);

    public Task<IReadOnlyList<Hephaestus.Core.Signals.StoredSignal>> ReadPendingAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Hephaestus.Core.Signals.StoredSignal>>([.. Stored.Where(s => s.Status == Hephaestus.Core.Signals.SignalStatus.Pending).Take(limit)]);
}

/// <summary>The poller's cursors in memory.</summary>
internal sealed class FakeCursors : Hephaestus.Core.Ports.IPollCursorStore
{
    public Dictionary<(long RepositoryId, string Stream), Hephaestus.Core.Ports.PollCursor> Stored { get; } = [];

    /// <summary>When set, storing a cursor fails with it, as a crash right after the append would.</summary>
    public Exception? Fail { get; set; }

    public Task<Hephaestus.Core.Ports.PollCursor?> GetAsync(long repositoryId, string stream, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.GetValueOrDefault((repositoryId, stream)));

    public Task SetAsync(long repositoryId, string stream, Hephaestus.Core.Ports.PollCursor cursor, CancellationToken cancellationToken)
    {
        if (Fail is not null)
        {
            throw Fail;
        }

        Stored[(repositoryId, stream)] = cursor;
        return Task.CompletedTask;
    }
}
