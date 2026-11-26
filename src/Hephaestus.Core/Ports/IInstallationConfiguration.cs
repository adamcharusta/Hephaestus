namespace Hephaestus.Core.Ports;

/// <summary>
/// The writes of Installation Configuration (AD-16): API handlers and adapters write it directly, each in its own transaction, and
/// everything reads it through <see cref="IReadStore"/>. Each story that owns a setting adds its method here.
/// </summary>
public interface IInstallationConfiguration
{
    /// <summary>Records the id of Hephaestus's own GitHub App, replacing an earlier one.</summary>
    Task SetGitHubAppIdAsync(long appId, CancellationToken cancellationToken);
}
