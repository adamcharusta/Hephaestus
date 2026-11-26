namespace Hephaestus.Core.Trust;

/// <summary>A GitHub user as GitHub names them: the numeric id and the login it has now.</summary>
/// <param name="IsHephaestus">Whether this is the bot account of Hephaestus's own GitHub App (AD-15).</param>
public sealed record GitHubUser(long Id, string Login, bool IsHephaestus);

/// <summary>Why a user could not be looked up.</summary>
public enum GitHubUserFailure
{
    /// <summary>The user was found.</summary>
    None,

    /// <summary>GitHub has no such user, or the account is not a user that can act (an organization).</summary>
    NotFound,

    /// <summary>No App id and key are stored, so GitHub is not asked.</summary>
    NotConnected,

    /// <summary>The stored key is not a PEM-encoded RSA private key.</summary>
    KeyUnreadable,

    /// <summary>GitHub answered and refused the stored credential.</summary>
    Rejected,

    /// <summary>GitHub did not answer, or answered that it could not serve the request.</summary>
    Unreachable,
}

/// <summary>The user, or the reason there is none.</summary>
public sealed record GitHubUserResult(GitHubUserFailure Failure, GitHubUser? User)
{
    public static GitHubUserResult Failed(GitHubUserFailure failure) => new(failure, null);

    public static GitHubUserResult Found(GitHubUser user) => new(GitHubUserFailure.None, user);
}
