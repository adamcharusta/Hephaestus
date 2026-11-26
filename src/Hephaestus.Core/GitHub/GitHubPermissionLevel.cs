namespace Hephaestus.Core.GitHub;

/// <summary>The level at which GitHub grants a permission to an App, in the order of GitHub's own: each includes the one before.</summary>
public enum GitHubPermissionLevel
{
    /// <summary>The permission is not granted.</summary>
    None,

    /// <summary>The App may read.</summary>
    Read,

    /// <summary>The App may read and write.</summary>
    Write,

    /// <summary>The App may also administer.</summary>
    Admin,
}
