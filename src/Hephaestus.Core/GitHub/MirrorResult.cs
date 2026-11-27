using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.GitHub;

/// <summary>Why a mirror could not be brought up to date, or a bundle could not be made from it.</summary>
public enum MirrorFailure
{
    /// <summary>It was done.</summary>
    None,

    /// <summary>No App id and key are stored, so GitHub is not asked. The Owner can mend this, so it is tried again.</summary>
    NotConnected,

    /// <summary>The stored key is not a PEM-encoded RSA private key. The Owner can mend this, so it is tried again.</summary>
    KeyUnreadable,

    /// <summary>The Project is not registered, or was removed.</summary>
    UnknownProject,

    /// <summary>The App is not installed on the repository any more. GitHub refuses for good.</summary>
    NotInstalled,

    /// <summary>GitHub did not answer, answered that it could not serve the request, or asked Hephaestus to wait.</summary>
    Unreachable,

    /// <summary>GitHub answered and refused the credential, or git could not fetch the repository.</summary>
    Rejected,

    /// <summary>The commit is not in the mirror, not even after the mirror was fetched again: it was removed from the repository, or never was in it.</summary>
    CommitMissing,

    /// <summary>The bundle is larger than the bound, so it is not made.</summary>
    TooLarge,

    /// <summary>Git failed in a way that is not the repository's or GitHub's doing: the executable is missing, the directory cannot be written, or the process ended abnormally.</summary>
    GitFailed,
}

/// <summary>The outcome of bringing a mirror up to date. <paramref name="Detail"/> is git's own report of what failed, masked and bounded, for the log and never for GitHub.</summary>
public sealed record MirrorResult(MirrorFailure Failure, string? Detail = null)
{
    public static MirrorResult Fetched { get; } = new(MirrorFailure.None);

    public static MirrorResult Failed(MirrorFailure failure, string? detail = null) => new(failure, detail);
}

/// <summary>The outcome of making a bundle: the reference to the file, or why there is none.</summary>
public sealed record BundleResult(MirrorFailure Failure, BundleReference? Bundle, string? Detail = null)
{
    public static BundleResult Made(BundleReference bundle) => new(MirrorFailure.None, bundle);

    public static BundleResult Failed(MirrorFailure failure, string? detail = null) => new(failure, null, detail);
}
