using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.GitHub;

/// <summary>
/// The pull request of a Task to publish (AD-4, FR-36): from the Task branch to the Base Branch, under Hephaestus's own identity. Only a Task branch can be its head, so that no request exists for a pull request from any other ref.
/// The text is composed by Core; the gateway masks stored secret values in it last and adds nothing to it.
/// </summary>
public sealed record PullRequestPublication
{
    public PullRequestPublication(long repositoryId, string head, string baseBranch, string title, string body)
    {
        if (!BranchName.IsTaskBranch(head))
        {
            throw new ArgumentException($"'{head}' is not the name of a Task branch; a pull request is opened from nothing else.", nameof(head));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(baseBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        RepositoryId = repositoryId;
        Head = head;
        Base = baseBranch;
        Title = title;
        Body = body ?? string.Empty;
    }

    /// <summary>GitHub's numeric id of the Project's repository.</summary>
    public long RepositoryId { get; }

    /// <summary>The Task branch, <c>hephaestus/&lt;issue-number&gt;-&lt;slug&gt;</c>.</summary>
    public string Head { get; }

    /// <summary>The Project's Base Branch.</summary>
    public string Base { get; }

    public string Title { get; }

    public string Body { get; }
}

/// <summary>What publishing did to the pull request of a branch.</summary>
public enum PullRequestOutcome
{
    /// <summary>The branch had no open pull request, and one was opened.</summary>
    Opened,

    /// <summary>The branch had an open pull request, found by its head branch, and its title and description were brought up to date.</summary>
    Updated,

    /// <summary>It was not published; <see cref="PullRequestResult.Failure"/> says why.</summary>
    Failed,
}

/// <summary>The result of publishing a pull request, or why it could not be.</summary>
/// <param name="Number">The pull request's number; 0 when it was not published.</param>
/// <param name="WaitUntil">When GitHub's rate limit lifts, for <see cref="GitHubIssueFailure.RateLimited"/>.</param>
public sealed record PullRequestResult(PullRequestOutcome Outcome, GitHubIssueFailure Failure, int Number = 0, DateTimeOffset? WaitUntil = null)
{
    public static PullRequestResult Opened(int number) => new(PullRequestOutcome.Opened, GitHubIssueFailure.None, number);

    public static PullRequestResult Updated(int number) => new(PullRequestOutcome.Updated, GitHubIssueFailure.None, number);

    public static PullRequestResult Failed(GitHubIssueFailure failure, DateTimeOffset? waitUntil = null) => new(PullRequestOutcome.Failed, failure, WaitUntil: waitUntil);
}
