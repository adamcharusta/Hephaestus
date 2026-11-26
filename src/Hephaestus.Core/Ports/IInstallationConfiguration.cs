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
}
