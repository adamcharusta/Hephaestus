using Hephaestus.Core.Invocations;

namespace Hephaestus.Core.Workspaces;

/// <summary>
/// A probe of an Agent credential for the runtime to run (AD-20): the invocation the adapter described, in a container that has no Task volume, no context, and no Methodology, and
/// whose only way out is the egress proxy, limited to the hosts the invocation declares. The probe is not a step run and is journaled nowhere.
/// </summary>
/// <param name="ProbeId">Names the probe's container, network, and proxy. It is new for every probe, so that nothing of an earlier probe is mistaken for this one's; what a crash left carries the labels of the installation and the role <c>agent-probe</c>, by which recovery finds it.</param>
/// <param name="Invocation">What <c>IAgentAdapter.DescribeProbe</c> described.</param>
/// <param name="Limits">The processor and memory the probe's container may use.</param>
/// <param name="Limit">How long the probe may take before the runtime removes its container.</param>
public sealed record ProbeRequest(Guid ProbeId, AgentInvocation Invocation, StepLimits Limits, TimeSpan Limit);

/// <summary>How a probe ended, as the runtime saw it (AD-18). <see cref="Lines"/> is what the container printed, masked and bounded, for the adapter to read.</summary>
/// <param name="ExitCode">The exit code; meaningless when <see cref="Cause"/> is not <see cref="StepExitCause.Exited"/>.</param>
public sealed record ProbeResult(long ExitCode, StepExitCause Cause, IReadOnlyList<string> Lines);
