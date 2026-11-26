using Hephaestus.Core.Aliases;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Trust;

namespace Hephaestus.Core.Ports;

/// <summary>
/// Every read of stored data goes through here (AD-17). Persistence implements it on a read-only SQLite connection, so a read
/// can never change anything and no endpoint depends on <c>IUnitOfWork</c>.
/// </summary>
public interface IReadStore
{
    /// <summary>The names of the stored secrets in ascending order, and nothing else about them.</summary>
    Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken cancellationToken);

    /// <summary>The id of Hephaestus's own GitHub App, or null when none was connected.</summary>
    Task<long?> GetGitHubAppIdAsync(CancellationToken cancellationToken);

    /// <summary>The Trusted Users, ascending by login and then by id.</summary>
    Task<IReadOnlyList<TrustedUser>> ListTrustedUsersAsync(CancellationToken cancellationToken);

    /// <summary>The Aliases, ascending by name.</summary>
    Task<IReadOnlyList<Alias>> ListAliasesAsync(CancellationToken cancellationToken);

    /// <summary>The installation limits; a limit nobody set is null.</summary>
    Task<InstallationLimits> GetLimitsAsync(CancellationToken cancellationToken);

    /// <summary>The protected paths, ascending.</summary>
    Task<IReadOnlyList<string>> ListProtectedPathsAsync(CancellationToken cancellationToken);

    /// <summary>The settings the Owner has set, by name; a setting never set is absent and has its default.</summary>
    Task<IReadOnlyDictionary<string, string>> ListSettingsAsync(CancellationToken cancellationToken);
}
