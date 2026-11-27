namespace Hephaestus.Core.GitHub;

/// <summary>Why a read or a change of an issue's labels did not happen.</summary>
public enum GitHubIssueFailure
{
    /// <summary>It happened.</summary>
    None,

    /// <summary>GitHub has no such issue, or no such repository: the issue was deleted or the repository is gone. GitHub refuses for good.</summary>
    IssueNotFound,

    /// <summary>The GitHub App is not installed on the repository any more. GitHub refuses for good.</summary>
    NotInstalled,

    /// <summary>GitHub answered and refused, for example because the App's permission to write issues was withdrawn. It refuses for good.</summary>
    Rejected,

    /// <summary>No App id and key are stored, so GitHub is not asked. The Owner can mend this, so it is tried again.</summary>
    NotConnected,

    /// <summary>The stored key is not a PEM-encoded RSA private key. The Owner can mend this, so it is tried again.</summary>
    KeyUnreadable,

    /// <summary>GitHub did not answer, or answered that it could not serve the request.</summary>
    Unreachable,

    /// <summary>GitHub asked Hephaestus to wait; nothing was sent. This is a wait and not a failure.</summary>
    RateLimited,
}

/// <summary>The labels of an issue as GitHub holds them (after a change, as it left them), or the reason they could not be read or changed.</summary>
/// <param name="WaitUntil">When GitHub's rate limit lifts, for <see cref="GitHubIssueFailure.RateLimited"/>.</param>
public sealed record GitHubIssueResult(GitHubIssueFailure Failure, IReadOnlyList<string> Labels, DateTimeOffset? WaitUntil = null)
{
    public static GitHubIssueResult Failed(GitHubIssueFailure failure, DateTimeOffset? waitUntil = null) => new(failure, [], waitUntil);

    public static GitHubIssueResult Labelled(IReadOnlyList<string> labels) => new(GitHubIssueFailure.None, labels);
}
