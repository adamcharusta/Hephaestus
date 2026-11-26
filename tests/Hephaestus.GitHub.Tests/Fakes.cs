using Hephaestus.Core.Ports;

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
}
