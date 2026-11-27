using Hephaestus.Core.GitHub;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Trust;

namespace Hephaestus.Core.Ports;

/// <summary>
/// The one gateway to GitHub (AD-4), implemented by <c>Hephaestus.GitHub</c>. It reads Hephaestus's own identity, users, repositories,
/// and files, and, as the stories that need them arrive, makes effects, each taking its idempotency key (AD-5): the labels of an issue
/// are the first.
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

    /// <summary>
    /// Reads the file at <paramref name="path"/> of the repository with GitHub's id <paramref name="repositoryId"/> (the name a Project is
    /// shown by may be stale), as it is at the tip of <paramref name="branch"/>: the raw bytes and the commit id they belong to, and
    /// nothing else of any other branch (AD-16). A file larger than <paramref name="maximumLength"/> is not read. The gateway parses nothing.
    /// </summary>
    Task<GitHubFileResult> ReadFileAsync(long repositoryId, string branch, string path, int maximumLength, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the labels the issue has now. A read is not an effect (AD-5); the loop uses it to resolve an effect that was started and not
    /// recorded, and to see what a change must do.
    /// </summary>
    Task<GitHubIssueResult> ReadIssueLabelsAsync(long repositoryId, int issueNumber, CancellationToken cancellationToken);

    /// <summary>
    /// An effect (AD-5): adds the labels in <paramref name="add"/> to the issue and removes those in <paramref name="remove"/>, and answers with the
    /// labels the issue has afterwards. A label already added, or already removed, is not an error, so the same change made twice is one change.
    /// <paramref name="idempotencyKey"/> is the key the loop journaled for the effect; the gateway neither journals nor derives keys.
    /// </summary>
    Task<GitHubIssueResult> ChangeIssueLabelsAsync(
        long repositoryId,
        int issueNumber,
        IReadOnlyList<string> add,
        IReadOnlyList<string> remove,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
