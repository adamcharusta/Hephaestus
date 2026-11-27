using Hephaestus.Core.Aliases;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Trust;

namespace Hephaestus.Host.Tests;

/// <summary>The ports a poller reads and writes, empty and in memory: no Project is registered, so a cycle has nothing to do.</summary>
internal sealed class PollerStores : IReadStore, ISecretStore, ISignalInbox, IPollCursorStore
{
    /// <summary>How many times the poller asked which Projects are registered: one for each cycle it ran.</summary>
    public int ProjectListings { get; private set; }

    public Task<IReadOnlyList<Project>> ListProjectsAsync(bool includeRemoved, CancellationToken cancellationToken)
    {
        ProjectListings++;
        return Task.FromResult<IReadOnlyList<Project>>([]);
    }

    public Task<IReadOnlyDictionary<string, string>> ListSettingsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

    public Task<long?> GetGitHubAppIdAsync(CancellationToken cancellationToken) => Task.FromResult<long?>(null);

    public Task<string?> ReadAsync(string name, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    public Task<int> AppendAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken) => Task.FromResult(0);

    public Task<IReadOnlyList<StoredSignal>> ReadAsync(long afterSequence, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<StoredSignal>>([]);

    public Task<PollCursor?> GetAsync(long repositoryId, string stream, CancellationToken cancellationToken) => Task.FromResult<PollCursor?>(null);

    public Task SetAsync(long repositoryId, string stream, PollCursor cursor, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<TrustedUser>> ListTrustedUsersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TrustedUser>>([]);

    public Task<IReadOnlyList<Alias>> ListAliasesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Alias>>([]);

    public Task<InstallationLimits> GetLimitsAsync(CancellationToken cancellationToken) => Task.FromResult(InstallationLimits.None);

    public Task<IReadOnlyList<string>> ListProtectedPathsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

    public Task SetAsync(string name, string value, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> RemoveAsync(string name, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<IReadOnlyList<string>> ReadAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

    public event Func<Task>? Changed
    {
        add { }
        remove { }
    }
}
