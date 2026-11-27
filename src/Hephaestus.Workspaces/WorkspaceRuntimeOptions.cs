using Hephaestus.Core.AgentBinaries;

namespace Hephaestus.Workspaces;

/// <summary>What the Docker runtime is told about the installation it serves (AD-7, AD-8).</summary>
/// <param name="InstallationId">The value of the <c>hephaestus.installation</c> label on every Docker object the runtime creates.</param>
/// <param name="AgentLock">The pinned Agent CLIs; it decides which builds are mounted into a step container.</param>
/// <param name="Architecture">The processor architecture of this machine, which every image of a Task must be built for.</param>
public sealed record WorkspaceRuntimeOptions(string InstallationId, AgentLock AgentLock, AgentArchitecture Architecture)
{
    /// <summary>The volume the host fills with the Agent CLIs (<c>hephaestus-agents</c>).</summary>
    public string AgentsVolume { get; init; } = "hephaestus-agents";

    /// <summary>The Docker network every step container is attached to, and to no other: it has no route out (AD-7).</summary>
    public string WorkspaceNetwork { get; init; } = "hephaestus-workspace";
}
