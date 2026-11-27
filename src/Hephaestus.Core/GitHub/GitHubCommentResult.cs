namespace Hephaestus.Core.GitHub;

/// <summary>
/// What GitHub said about the comment of an effect (AD-5): that it is there, with its id, that it is not, or why that could not be learned or
/// the comment could not be posted.
/// </summary>
/// <param name="CommentId">The id of the comment Hephaestus's own account wrote with the effect's marker; null when there is none.</param>
/// <param name="WaitUntil">When GitHub's rate limit lifts, for <see cref="GitHubIssueFailure.RateLimited"/>.</param>
public sealed record GitHubCommentResult(GitHubIssueFailure Failure, long? CommentId = null, DateTimeOffset? WaitUntil = null)
{
    /// <summary>The comment is not there.</summary>
    public static GitHubCommentResult Absent { get; } = new(GitHubIssueFailure.None);

    public static GitHubCommentResult Present(long commentId) => new(GitHubIssueFailure.None, commentId);

    public static GitHubCommentResult Failed(GitHubIssueFailure failure, DateTimeOffset? waitUntil = null) => new(failure, WaitUntil: waitUntil);
}
