using Hephaestus.Core.GitHub;

namespace Hephaestus.Core.Ports;

/// <summary>
/// The pull requests of Tasks on GitHub (AD-4, FR-36), implemented by <c>Hephaestus.GitHub</c>. A pull request is opened under Hephaestus's own identity, from a Task branch to the Project's Base Branch; the gateway has no code path that
/// merges, closes, or opens one from any other branch (FR-44).
/// </summary>
public interface IPullRequests
{
    /// <summary>
    /// An effect (AD-5): publishes the pull request of the Task branch. The branch is looked at first, and the open pull request it has, found by its head branch, has its title and description brought up to date, so that one made before a crash
    /// and not recorded is found and never opened twice (FR-21); only when it has none is one opened. Stored secret values are masked in the title and the description. <paramref name="idempotencyKey"/> is the key the loop journaled; the
    /// gateway neither journals nor derives keys.
    /// </summary>
    Task<PullRequestResult> PublishAsync(PullRequestPublication request, string idempotencyKey, CancellationToken cancellationToken);
}
