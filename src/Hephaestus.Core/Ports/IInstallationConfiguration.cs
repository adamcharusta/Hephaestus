using Hephaestus.Core.Aliases;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Trust;

namespace Hephaestus.Core.Ports;

/// <summary>
/// The writes of Installation Configuration (AD-16): API handlers and adapters write it directly, each in its own transaction, and
/// everything reads it through <see cref="IReadStore"/>. Each story that owns a setting adds its method here.
/// </summary>
public interface IInstallationConfiguration
{
    /// <summary>Records the id of Hephaestus's own GitHub App, replacing an earlier one.</summary>
    Task SetGitHubAppIdAsync(long appId, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a Trusted User by GitHub's numeric id (AD-15). A user already stored keeps their id and the time they were added, and
    /// the login, which is display only, is refreshed.
    /// </summary>
    Task<TrustedUserWrite> AddTrustedUserAsync(long userId, string login, CancellationToken cancellationToken);

    /// <summary>Removes the Trusted User with that id; false when none was stored.</summary>
    Task<bool> RemoveTrustedUserAsync(long userId, CancellationToken cancellationToken);

    /// <summary>
    /// Stores an Alias. An Alias of that name already stored is returned unchanged with <see cref="AliasWrite.Added"/> false; Core
    /// decides whether that is the same Alias again or a conflict.
    /// </summary>
    Task<AliasWrite> AddAliasAsync(Alias alias, CancellationToken cancellationToken);

    /// <summary>Removes the Alias of that name and returns it; null when none was stored.</summary>
    Task<Alias?> RemoveAliasAsync(string name, CancellationToken cancellationToken);

    /// <summary>Changes the limits that <paramref name="change"/> names and keeps the others; returns the limits as stored now.</summary>
    Task<InstallationLimits> SetLimitsAsync(LimitsChange change, CancellationToken cancellationToken);

    /// <summary>Adds a protected path; false when it was already listed.</summary>
    Task<bool> AddProtectedPathAsync(string glob, CancellationToken cancellationToken);

    /// <summary>Removes a protected path; false when it was not listed.</summary>
    Task<bool> RemoveProtectedPathAsync(string glob, CancellationToken cancellationToken);

    /// <summary>Stores the value of an installation setting, replacing an earlier one. Core has checked the name and the value.</summary>
    Task SetSettingAsync(string name, string value, CancellationToken cancellationToken);

    /// <summary>
    /// Registers a repository as a Project, by GitHub's numeric id. A repository already registered keeps its id and the time it was
    /// registered, and its name and Base Branch are replaced; a removed one is registered again from its retained record.
    /// </summary>
    Task<ProjectWrite> RegisterProjectAsync(long repositoryId, string fullName, string baseBranch, CancellationToken cancellationToken);

    /// <summary>Marks the registered Project with that repository id as removed and keeps everything else; null when it is not registered.</summary>
    Task<Project?> RemoveProjectAsync(long repositoryId, CancellationToken cancellationToken);
}
