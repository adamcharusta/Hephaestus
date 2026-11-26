namespace Hephaestus.Core.Projects;

/// <summary>A branch of a repository as GitHub reports it.</summary>
/// <param name="IsProtected">Whether GitHub reports the branch as protected. The App has no administration permission, so what the protection requires is not read.</param>
public sealed record GitHubBranch(string Name, bool IsProtected);

/// <summary>A repository the GitHub App is installed on: GitHub's numeric id, the name it has now, and the branch that was asked for.</summary>
public sealed record GitHubRepository(long Id, string FullName, GitHubBranch? Branch);

/// <summary>Why a repository could not be found.</summary>
public enum GitHubRepositoryFailure
{
    /// <summary>The repository was found.</summary>
    None,

    /// <summary>The App is not installed on the repository, or the repository does not exist; GitHub does not tell the two apart to an App.</summary>
    NotInstalled,

    /// <summary>The App is installed on the repository and the repository has no such branch.</summary>
    BranchNotFound,

    /// <summary>No App id and key are stored, so GitHub is not asked.</summary>
    NotConnected,

    /// <summary>The stored key is not a PEM-encoded RSA private key.</summary>
    KeyUnreadable,

    /// <summary>GitHub answered and refused the stored credential.</summary>
    Rejected,

    /// <summary>GitHub did not answer, or answered that it could not serve the request.</summary>
    Unreachable,
}

/// <summary>The repository, or the reason there is none.</summary>
public sealed record GitHubRepositoryResult(GitHubRepositoryFailure Failure, GitHubRepository? Repository)
{
    public static GitHubRepositoryResult Failed(GitHubRepositoryFailure failure) => new(failure, null);

    public static GitHubRepositoryResult Found(GitHubRepository repository) => new(GitHubRepositoryFailure.None, repository);
}
