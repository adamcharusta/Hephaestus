using Hephaestus.Core.Projects;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Validation;

/// <summary>
/// What the runtime needs to run one Validation command (AD-6, AD-13, FR-31): the Task and the container run of this command, the Task branch and the bundle of the pushed tip it is run on, the image the Task runs in, the
/// command, its limits and deadline, and the Project's egress list. Each command is its own container run, so each has a step run of its own, whose name and output file nothing else shares. The container it makes holds
/// no credential, no context, no Methodology, and no Agent CLI, and may reach the Project's listed hosts and no other (<see cref="EgressPolicy"/>).
/// </summary>
public sealed record ValidationCommandRequest
{
    public ValidationCommandRequest(
        Guid taskId,
        Guid stepRunId,
        string branch,
        BundleReference tip,
        ResolvedImage image,
        string command,
        StepLimits limits,
        IReadOnlyList<string> projectEgressHosts,
        DateTimeOffset deadline)
    {
        ArgumentNullException.ThrowIfNull(tip);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(projectEgressHosts);
        if (!BranchName.IsTaskBranch(branch))
        {
            throw new ArgumentException($"'{branch}' is not the name of a Task branch.", nameof(branch));
        }

        if (tip.Prerequisite is not null)
        {
            throw new ArgumentException("The tip is the bundle of the last pushed commit, which is whole.", nameof(tip));
        }

        if (string.IsNullOrWhiteSpace(command) || command.Length > ProjectConfigurationFile.MaximumCommandLength)
        {
            throw new ArgumentException($"A Validation command is not blank and has at most {ProjectConfigurationFile.MaximumCommandLength} characters.", nameof(command));
        }

        TaskId = taskId;
        StepRunId = stepRunId;
        Branch = branch;
        Tip = tip;
        Image = image;
        Command = command;
        Limits = limits;
        ProjectEgressHosts = projectEgressHosts;
        Deadline = deadline;
    }

    public Guid TaskId { get; }

    /// <summary>The container run of this command, which names its containers and keys its output.</summary>
    public Guid StepRunId { get; }

    public string Branch { get; }

    /// <summary>The bundle of the commit that is the Task branch's last pushed tip, which the command is run on.</summary>
    public BundleReference Tip { get; }

    public ResolvedImage Image { get; }

    /// <summary>The command as the Project wrote it; it is run through <c>bash</c>.</summary>
    public string Command { get; }

    public StepLimits Limits { get; }

    /// <summary>The Project's <c>allowedEgressHosts</c>; empty when it sets none.</summary>
    public IReadOnlyList<string> ProjectEgressHosts { get; }

    /// <summary>When the runtime stops the container if it still runs (FR-19). The loop records it before it asks for the container.</summary>
    public DateTimeOffset Deadline { get; }

    /// <summary>The fresh checkout the command is run on: <c>/workspace</c> made again from the bundle of the tip, so that nothing an earlier command or step left in the Task's volume is there (AD-6).</summary>
    public CheckoutRequest Checkout() => new(TaskId, StepRunId, Branch, Tip);

    /// <summary>
    /// The container that runs the command: <c>bash -c</c> and the command, in the Task's image, with no secret, no file, and no environment of its own, and with the Project's egress list alone, never an Agent's (AD-6, AD-7). What
    /// it leaves in the tree is no one's work: Validation is never checkpointed.
    /// </summary>
    public StepContainerRequest Container() => new(
        TaskId,
        StepRunId,
        Image,
        ["bash", "-c", Command],
        new Dictionary<string, string>(),
        Limits,
        [],
        [],
        Deadline,
        EgressPolicy.AllowedHosts(ProjectEgressHosts, invocationHosts: null),
        SecretReferences: null,
        IsValidation: true);
}
