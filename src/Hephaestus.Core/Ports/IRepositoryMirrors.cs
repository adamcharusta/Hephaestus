using Hephaestus.Core.GitHub;

namespace Hephaestus.Core.Ports;

/// <summary>
/// The bare mirrors of the Projects' repositories, which only the GitHub gateway owns (AD-4, AD-9). A mirror has no hooks and no working tree, so git can be run in it;
/// the orchestrator runs git nowhere else, and never in a directory an Agent can write to. Code leaves the mirror only as a bundle, a file of the data volume that the
/// runtime hands to a checkout container (AD-6). Nothing here is an effect on GitHub: it only reads, and the mirror can be rebuilt from GitHub at any time.
/// </summary>
public interface IRepositoryMirrors
{
    /// <summary>
    /// Brings the mirror of the Project's repository up to date with GitHub, creating it when there is none: every branch, with the branches that are gone from the
    /// repository removed. The installation token that reads the repository is given to one git process through its environment and is written nowhere.
    /// </summary>
    Task<MirrorResult> FetchAsync(long repositoryId, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a bundle of one commit of the mirror to the bundles directory, with every object that commit needs and one head, and says what it holds. A commit the
    /// mirror lacks is looked for by fetching the mirror once. The file is made under a temporary name and moved into place, so that a crash leaves no half-written bundle,
    /// and it is the same file for the same step run, so that a retry replaces it.
    /// </summary>
    Task<BundleResult> CreateBundleAsync(BundleRequest request, CancellationToken cancellationToken);
}
