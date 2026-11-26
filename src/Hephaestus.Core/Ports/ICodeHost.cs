using Hephaestus.Core.GitHub;

namespace Hephaestus.Core.Ports;

/// <summary>
/// The one gateway to GitHub (AD-4), implemented by <c>Hephaestus.GitHub</c>. This story gives it the two reads of Hephaestus's
/// own identity; the effecting methods arrive with the stories that need them, each taking its idempotency key (AD-5).
/// </summary>
public interface ICodeHost
{
    /// <summary>
    /// Authenticates as the App with <paramref name="appId"/> and <paramref name="privateKey"/> and reads its identity. Only when
    /// GitHub accepted both are they stored: the key as the secret <see cref="GitHubAppCredential.PrivateKeySecretName"/> and
    /// the App id as Installation Configuration. A failure, or a store that fails half way, leaves what was stored before.
    /// </summary>
    Task<GitHubIdentityResult> ConnectIdentityAsync(long appId, string privateKey, CancellationToken cancellationToken);

    /// <summary>Reads the identity of the App whose credential is stored, from GitHub, now.</summary>
    Task<GitHubIdentityResult> CheckIdentityAsync(CancellationToken cancellationToken);
}
