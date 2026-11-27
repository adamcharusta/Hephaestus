namespace Hephaestus.Core.Projects;

/// <summary>A file of a repository as the gateway read it: its raw bytes, and the commit those bytes belong to (AD-16).</summary>
public sealed record GitHubFile(byte[] Content, string CommitId);

/// <summary>Why a file could not be read.</summary>
public enum GitHubFileFailure
{
    /// <summary>The file was read.</summary>
    None,

    /// <summary>The branch has no such file (or the path is a directory, a link, or a submodule). The commit that was looked at is known.</summary>
    NotFound,

    /// <summary>The file is larger than the caller allows, so it was not read. The commit that was looked at is known.</summary>
    TooLarge,

    /// <summary>The GitHub App is not installed on the repository any more, or the repository is gone.</summary>
    NotInstalled,

    /// <summary>The repository has no such branch.</summary>
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

/// <summary>The file, or the reason there is none.</summary>
/// <param name="CommitId">The tip of the branch that was read, known whenever the branch was found.</param>
public sealed record GitHubFileResult(GitHubFileFailure Failure, GitHubFile? File, string? CommitId)
{
    public static GitHubFileResult Failed(GitHubFileFailure failure, string? commitId = null) => new(failure, null, commitId);

    public static GitHubFileResult Read(GitHubFile file) => new(GitHubFileFailure.None, file, file.CommitId);
}
