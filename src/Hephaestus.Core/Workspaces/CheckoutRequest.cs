using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Workspaces;

/// <summary>
/// What the runtime needs to make a fresh checkout before a step (AD-6, FR-17): the Task and the step run it is for, the Task's branch, and the bundle of the commit the
/// step starts from, which is the Task's last pushed tip, or its start commit on the Base Branch when nothing was pushed yet. The gateway makes the bundle
/// (<see cref="Ports.IRepositoryMirrors"/>); the runtime consumes it and deletes the file.
/// </summary>
/// <param name="Branch">The Task branch (<see cref="BranchName"/>), which is checked out at the bundle's tip.</param>
public sealed record CheckoutRequest
{
    public CheckoutRequest(Guid taskId, Guid stepRunId, string branch, BundleReference bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (!BranchName.IsTaskBranch(branch))
        {
            throw new ArgumentException($"'{branch}' is not the name of a Task branch.", nameof(branch));
        }

        TaskId = taskId;
        StepRunId = stepRunId;
        Branch = branch;
        Bundle = bundle;
    }

    public Guid TaskId { get; }

    public Guid StepRunId { get; }

    public string Branch { get; }

    public BundleReference Bundle { get; }
}
