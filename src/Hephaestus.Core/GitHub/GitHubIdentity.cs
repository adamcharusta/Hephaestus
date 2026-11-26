namespace Hephaestus.Core.GitHub;

/// <summary>One required permission of the App, what it needs, what it has, and whether that is enough (AD-19).</summary>
/// <param name="Name">GitHub's permission, in kebab-case: <c>contents</c>, <c>pull-requests</c>. Never translated.</param>
public sealed record GitHubPermissionCheck(string Name, GitHubPermissionLevel Required, GitHubPermissionLevel Granted, bool Present);

/// <summary>A permission the App must not have, and the level at which it has it.</summary>
public sealed record GitHubPermissionWarning(string Name, GitHubPermissionLevel Granted);

/// <summary>One place the App is installed, with the repositories it can reach there.</summary>
/// <param name="Account">The login of the user or organization that installed it.</param>
/// <param name="AccountType">GitHub's account type, for example <c>User</c> or <c>Organization</c>.</param>
/// <param name="RepositorySelection">GitHub's word for the reach: <c>all</c> or <c>selected</c>.</param>
/// <param name="Suspended">Whether the installation is suspended; its repositories are then not listed.</param>
/// <param name="Repositories">Full names, <c>owner/repo</c>, in ascending order.</param>
public sealed record GitHubInstallation(long Id, string Account, string AccountType, string RepositorySelection, bool Suspended, IReadOnlyList<string> Repositories);

/// <summary>What GitHub says Hephaestus's own App is (AD-4): who it is, where it is installed, and what it may do.</summary>
public sealed record GitHubIdentity(
    long AppId,
    string Slug,
    string Name,
    string Owner,
    IReadOnlyList<GitHubInstallation> Installations,
    IReadOnlyList<GitHubPermissionCheck> Permissions,
    IReadOnlyList<GitHubPermissionWarning> Warnings)
{
    /// <summary>Whether every required permission is present.</summary>
    public bool Ready => Permissions.All(p => p.Present);
}

/// <summary>Why a GitHub identity could not be read.</summary>
public enum GitHubIdentityFailure
{
    /// <summary>It was read.</summary>
    None,

    /// <summary>No App id and key are stored.</summary>
    NotConnected,

    /// <summary>The key is not a PEM-encoded RSA private key, so GitHub was not asked.</summary>
    KeyUnreadable,

    /// <summary>GitHub answered and refused the App id and key.</summary>
    Rejected,

    /// <summary>GitHub did not answer, or answered that it could not serve the request.</summary>
    Unreachable,
}

/// <summary>The identity, or the reason there is none.</summary>
public sealed record GitHubIdentityResult(GitHubIdentityFailure Failure, GitHubIdentity? Identity)
{
    public static GitHubIdentityResult Failed(GitHubIdentityFailure failure) => new(failure, null);

    public static GitHubIdentityResult Succeeded(GitHubIdentity identity) => new(GitHubIdentityFailure.None, identity);
}
