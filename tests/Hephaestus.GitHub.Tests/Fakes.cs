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

    public Task<IReadOnlyDictionary<string, string>> ListSettingsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<AliasWrite> AddAliasAsync(Alias alias, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Alias?> RemoveAliasAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<InstallationLimits> SetLimitsAsync(LimitsChange change, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> AddProtectedPathAsync(string glob, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> RemoveProtectedPathAsync(string glob, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task SetSettingAsync(string name, string value, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<long?> GetGitHubAppIdAsync(CancellationToken cancellationToken) => Task.FromResult(AppId);

    public Task SetGitHubAppIdAsync(long appId, CancellationToken cancellationToken)
    {
        if (FailToRecordAppId is not null)
        {
            throw FailToRecordAppId;
        }

        AppId = appId;
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

    public Task<IReadOnlyList<Project>> ListProjectsAsync(bool includeRemoved, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ProjectWrite> RegisterProjectAsync(long repositoryId, string fullName, string baseBranch, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Project?> RemoveProjectAsync(long repositoryId, CancellationToken cancellationToken) => throw new NotSupportedException();
}
