using Hephaestus.Core.GitHub;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Trust;

namespace Hephaestus.Core.Ports;

/// <summary>
/// The one gateway to GitHub (AD-4), implemented by <c>Hephaestus.GitHub</c>. This story gives it the two reads of Hephaestus's
/// own identity and the lookup of a user; the effecting methods arrive with the stories that need them, each taking its idempotency key (AD-5).
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

    /// <summary>
    /// Resolves a login to GitHub's numeric user id (AD-15), authenticated as the connected App, and says whether it is the App's
    /// own bot account. An organization is not a user and is not found. Nothing is stored.
    /// </summary>
    Task<GitHubUserResult> FindUserAsync(string login, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a repository the connected App is installed on by its <c>owner/name</c>, and, when <paramref name="branch"/> is given,
    /// that branch of it and whether GitHub reports it as protected (AD-19). A repository the App is not installed on is not found,
    /// whether or not it exists. Nothing is stored.
    /// </summary>
    Task<GitHubRepositoryResult> FindRepositoryAsync(string fullName, string? branch, CancellationToken cancellationToken);
}
