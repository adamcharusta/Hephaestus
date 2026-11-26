using Hephaestus.Core.Aliases;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Trust;

namespace Hephaestus.Core.Tests;

/// <summary>The Installation Configuration of Story 1.9 in memory: the two ports as Persistence implements them, without a database.</summary>
internal sealed class FakeInstallation : IReadStore, IInstallationConfiguration
{
    public Dictionary<string, Alias> Aliases { get; } = [];

    public InstallationLimits Limits { get; set; } = InstallationLimits.None;

    public SortedSet<string> ProtectedPaths { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Settings { get; } = [];

    public Task<AliasWrite> AddAliasAsync(Alias alias, CancellationToken cancellationToken)
    {
        if (Aliases.TryGetValue(alias.Name, out var stored))
        {
            return Task.FromResult(new AliasWrite(stored, Added: false));
        }

        Aliases[alias.Name] = alias;
        return Task.FromResult(new AliasWrite(alias, Added: true));
    }

    public Task<Alias?> RemoveAliasAsync(string name, CancellationToken cancellationToken)
    {
        Aliases.Remove(name, out var removed);
        return Task.FromResult(removed);
    }

    public Task<InstallationLimits> SetLimitsAsync(LimitsChange change, CancellationToken cancellationToken)
    {
        Limits = new InstallationLimits(change.TaskDuration ?? Limits.TaskDuration, change.CpuMillis ?? Limits.CpuMillis, change.MemoryBytes ?? Limits.MemoryBytes);
        return Task.FromResult(Limits);
    }

    public Task<bool> AddProtectedPathAsync(string glob, CancellationToken cancellationToken) => Task.FromResult(ProtectedPaths.Add(glob));

    public Task<bool> RemoveProtectedPathAsync(string glob, CancellationToken cancellationToken) => Task.FromResult(ProtectedPaths.Remove(glob));

    public Task SetSettingAsync(string name, string value, CancellationToken cancellationToken)
    {
        Settings[name] = value;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Alias>> ListAliasesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Alias>>([.. Aliases.Values.OrderBy(a => a.Name, StringComparer.Ordinal)]);

    public Task<InstallationLimits> GetLimitsAsync(CancellationToken cancellationToken) => Task.FromResult(Limits);

    public Task<IReadOnlyList<string>> ListProtectedPathsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([.. ProtectedPaths]);

    public Task<IReadOnlyDictionary<string, string>> ListSettingsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Settings));

    public Task SetGitHubAppIdAsync(long appId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<TrustedUserWrite> AddTrustedUserAsync(long userId, string login, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> RemoveTrustedUserAsync(long userId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<long?> GetGitHubAppIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<TrustedUser>> ListTrustedUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
