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

    /// <summary>
    /// The Docker network with a route out, which the egress proxies are attached to besides the internal network of their step run (AR-41). No step container and
    /// not the <c>hephaestus</c> container is on it.
    /// </summary>
    public string OutboundNetwork { get; init; } = "hephaestus-outbound";

    /// <summary>The image the egress proxy of a step run is created from: the <c>hephaestus</c> image itself, whose <c>egress-proxy</c> command is the proxy (Story 3.4).</summary>
    public string EgressProxyImage { get; init; } = "ghcr.io/adamcharusta/hephaestus:latest";

    /// <summary>How long a step may wait for its egress proxy to start listening before it is not started.</summary>
    public TimeSpan EgressProxyReadyLimit { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How often the runtime looks for the proxy's line that says it is listening.</summary>
    public TimeSpan EgressProxyPollInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>How often the runtime looks at a running step container; the deadline is acted on when it passes, not at the next look (AD-18).</summary>
    public TimeSpan StepPollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How long the runtime waits before it tries again what failed while it watched a step: a call to Docker, or the store, or the inbox.</summary>
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a step's output may take to arrive after its container stopped, before what has arrived is all there is.</summary>
    public TimeSpan OutputDrainLimit { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The seconds a container that is stopped at its deadline is given to end before it is killed.</summary>
    public uint StopGraceSeconds { get; init; } = 10;
}
