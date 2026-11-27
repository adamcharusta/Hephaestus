using Hephaestus.Core.GitHub;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Ports;

/// <summary>
/// The Task branches on GitHub (AD-4, AD-9), implemented by <c>Hephaestus.GitHub</c>, the only code that pushes. A Task branch is pushed by fast-forward only, after the bundle of a checkpoint has passed every check; the gateway
/// has no code path that force-pushes, deletes a ref, or pushes anything that is not a Task branch, so no request for one can be made (FR-44, AR-53).
/// </summary>
public interface ITaskBranches
{
    /// <summary>
    /// Fetches the branch and says whether it is where Hephaestus left it: at the last pushed tip, or absent when nothing was pushed. A branch that is not holds the Task: no step runs on it and nothing is pushed.
    /// A read is not an effect (AD-5).
    /// </summary>
    Task<BranchCheckResult> CheckBranchAsync(BranchCheckRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// An effect (AD-5): imports the bundle into a quarantine ref with strict object checking, accepts only the Task's branch, requires a fast-forward of the last pushed tip, scans every new object with the masker, checks the
    /// diff against the start commit for protected paths, and pushes. The first check that fails blocks the push and nothing moves. After a crash the branch is read first: one that is at the tip, or beyond it, is
    /// <see cref="PushOutcome.AlreadyPushed"/> and nothing is pushed twice. <paramref name="idempotencyKey"/> is the key the loop journaled; the gateway neither journals nor derives keys.
    /// </summary>
    Task<PushResult> PushAsync(PushRequest request, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Deletes the file of a bundle the push has been journaled for. A file that is not there is not an error, and nothing outside the bundles directory is ever deleted.</summary>
    void DeleteBundle(BundleReference bundle);
}
