using Hephaestus.Core.Tasks;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.GitHub;

/// <summary>
/// A checkpoint bundle to push to the Task's branch (AD-9). The bundle's prerequisite is the last pushed tip, the commit the bundle must be a fast-forward of; the start commit is the commit on the Base Branch
/// that the Task began from, which the diff for the protected paths is made against (AD-19). Only a Task branch can be named, so that no request exists for any other ref (AD-4, AR-53).
/// </summary>
public sealed record PushRequest
{
    public PushRequest(long repositoryId, Guid taskId, Guid stepRunId, string branch, BundleReference bundle, string startCommit)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (!BranchName.IsTaskBranch(branch))
        {
            throw new ArgumentException($"'{branch}' is not the name of a Task branch; nothing else is pushed.", nameof(branch));
        }

        if (bundle.Prerequisite is null)
        {
            throw new ArgumentException("The bundle of a checkpoint has the last pushed tip as its prerequisite.", nameof(bundle));
        }

        if (!BundleReference.IsCommitId(startCommit))
        {
            throw new ArgumentException("The start commit is a commit id in 40 lower-case hexadecimal digits.", nameof(startCommit));
        }

        RepositoryId = repositoryId;
        TaskId = taskId;
        StepRunId = stepRunId;
        Branch = branch;
        Bundle = bundle;
        StartCommit = startCommit;
    }

    public long RepositoryId { get; }

    public Guid TaskId { get; }

    public Guid StepRunId { get; }

    /// <summary>The Task branch, <c>hephaestus/&lt;issue-number&gt;-&lt;slug&gt;</c>.</summary>
    public string Branch { get; }

    public BundleReference Bundle { get; }

    /// <summary>The commit on the Base Branch the Task began from.</summary>
    public string StartCommit { get; }

    /// <summary>The tip Hephaestus last pushed, or the start commit when nothing was pushed yet: the tip before this push.</summary>
    public string LastPushedTip => Bundle.Prerequisite!;

    /// <summary>The tip after this push.</summary>
    public string Tip => Bundle.Tip;
}

/// <summary>Where a Task branch has to be before a step runs or a push is made (AD-9): at the tip Hephaestus last pushed, or absent when nothing was pushed yet.</summary>
public sealed record BranchCheckRequest
{
    public BranchCheckRequest(long repositoryId, string branch, string lastPushedTip, string startCommit)
    {
        if (!BranchName.IsTaskBranch(branch))
        {
            throw new ArgumentException($"'{branch}' is not the name of a Task branch.", nameof(branch));
        }

        if (!BundleReference.IsCommitId(lastPushedTip))
        {
            throw new ArgumentException("The last pushed tip is a commit id in 40 lower-case hexadecimal digits.", nameof(lastPushedTip));
        }

        if (!BundleReference.IsCommitId(startCommit))
        {
            throw new ArgumentException("The start commit is a commit id in 40 lower-case hexadecimal digits.", nameof(startCommit));
        }

        RepositoryId = repositoryId;
        Branch = branch;
        LastPushedTip = lastPushedTip;
        StartCommit = startCommit;
    }

    public long RepositoryId { get; }

    public string Branch { get; }

    public string LastPushedTip { get; }

    public string StartCommit { get; }
}
