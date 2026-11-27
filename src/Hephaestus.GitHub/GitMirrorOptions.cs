namespace Hephaestus.GitHub;

/// <summary>Where the gateway keeps what it reads from git (AD-9, AR-39): the bare mirrors, which are the <c>hephaestus-mirrors</c> volume, and the bundles it writes, a directory of the data volume.</summary>
/// <param name="MirrorsDirectory">The directory that holds one bare mirror per repository, <c>&lt;repository id&gt;.git</c>; git runs nowhere else.</param>
/// <param name="BundlesDirectory">The directory the bundles are written to and the runtime reads them from.</param>
public sealed record GitMirrorOptions(string MirrorsDirectory, string BundlesDirectory)
{
    /// <summary>The git executable; the <c>hephaestus</c> image carries git 2.48 or later (AR-3).</summary>
    public string GitExecutable { get; init; } = "git";

    /// <summary>The protocols git may use, as <c>GIT_ALLOW_PROTOCOL</c> names them: only <c>https</c>, so that a repository cannot make git read a local path or run a helper.</summary>
    public string AllowedProtocols { get; init; } = "https";

    /// <summary>The address of a repository by its full name; GitHub's own, unless a test names another.</summary>
    public Func<string, string> RemoteUrl { get; init; } = fullName => $"https://github.com/{fullName}.git";

    /// <summary>The largest bundle the gateway writes; the runtime's bound is the same constant, <see cref="Hephaestus.Core.Workspaces.BundleReference.DefaultMaximumBytes"/>.</summary>
    public long MaximumBundleBytes { get; init; } = Hephaestus.Core.Workspaces.BundleReference.DefaultMaximumBytes;

    /// <summary>How long one fetch may take, one bundle, and any other git command; a git process that is still running then is killed.</summary>
    public TimeSpan FetchTimeout { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromMinutes(5);
}
