namespace Hephaestus.Core.Workspaces;

/// <summary>
/// Everything the runtime needs to start one step container (AD-7, AD-10): the Task and step run the container belongs to, the image the Task runs, the
/// invocation the adapter described, the limits, and the files placed read-only. The environment is written into the container's
/// configuration, which anyone with the Docker socket can read, so it holds no secret value: how a credential reaches a container is the story of the sign-in modes (3.10).
/// </summary>
/// <param name="StepRunId">The step run; at most one container exists for it.</param>
/// <param name="Image">The image the Task started with; every step of the Task runs it.</param>
/// <param name="Command">The argument vector, whole: the first element is the executable.</param>
/// <param name="Environment">The environment of the invocation, besides <c>HOME</c>.</param>
/// <param name="Context">The context files of the step run (AD-12).</param>
/// <param name="Methodology">The Methodology's files (AD-14).</param>
/// <param name="AllowedEgressHosts">The host names the step container may reach through the egress proxy, or null for any public host (AD-7); <see cref="EgressPolicy.AllowedHosts"/> makes it from the Project's list and the invocation's. Either way the address of a private network is refused.</param>
/// <param name="Deadline">When the runtime stops the container if it still runs (FR-19, AD-18). The loop records it on the step run before it asks for the container, so a restart knows it too; the runtime alone acts on it.</param>
public sealed record StepContainerRequest(
    Guid TaskId,
    Guid StepRunId,
    ResolvedImage Image,
    IReadOnlyList<string> Command,
    IReadOnlyDictionary<string, string> Environment,
    StepLimits Limits,
    IReadOnlyList<WorkspaceFile> Context,
    IReadOnlyList<WorkspaceFile> Methodology,
    DateTimeOffset Deadline,
    IReadOnlyList<string>? AllowedEgressHosts);

/// <summary>A step container that exists: its id and name, and whether this call made it (false when a restart found it already made).</summary>
public sealed record StartedStep(string ContainerId, string Name, bool Created);
