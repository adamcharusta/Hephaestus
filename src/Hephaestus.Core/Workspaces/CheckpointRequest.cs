using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Workspaces;

/// <summary>
/// What the runtime needs to checkpoint a step (AD-9, FR-20): the Task and the step run, the kind of step, the Task's branch, the bundle of the commit the step started from, which is the
/// Task's last pushed tip and the parent of the checkpoint, and the commit Core composed. The gateway makes the base bundle (<see cref="Ports.IRepositoryMirrors"/>); the runtime consumes it and deletes
/// the file, as it does for a checkout.
/// </summary>
public sealed record CheckpointRequest
{
    public CheckpointRequest(Guid taskId, Guid stepRunId, StepKind kind, string branch, BundleReference parent, CheckpointCommit commit)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(commit);
        if (!kind.IsCheckpointed())
        {
            throw new ArgumentException($"A step of kind '{kind.Tag()}' is not checkpointed: what it leaves in the tree is discarded.", nameof(kind));
        }

        if (!BranchName.IsTaskBranch(branch))
        {
            throw new ArgumentException($"'{branch}' is not the name of a Task branch.", nameof(branch));
        }

        if (parent.Prerequisite is not null)
        {
            throw new ArgumentException("The parent is the bundle of the last pushed tip, which is whole.", nameof(parent));
        }

        if (commit.TaskId != taskId || commit.StepRunId != stepRunId || commit.Kind != kind)
        {
            throw new ArgumentException("The commit was composed for another Task, step run, or kind of step, so its trailers would say what this checkpoint is not.", nameof(commit));
        }

        TaskId = taskId;
        StepRunId = stepRunId;
        Kind = kind;
        Branch = branch;
        Parent = parent;
        Commit = commit;
    }

    public Guid TaskId { get; }

    public Guid StepRunId { get; }

    public StepKind Kind { get; }

    public string Branch { get; }

    /// <summary>The bundle of the last pushed tip, whose commit is the parent of the checkpoint.</summary>
    public BundleReference Parent { get; }

    public CheckpointCommit Commit { get; }
}
