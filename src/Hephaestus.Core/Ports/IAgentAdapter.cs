using Hephaestus.Core.Aliases;
using Hephaestus.Core.Failures;
using Hephaestus.Core.Invocations;

namespace Hephaestus.Core.Ports;

/// <summary>
/// The pure adapter of one Agent (AD-10, AR-19): it describes, and starts nothing and knows nothing of Docker. It describes the invocation of a step and the
/// probe that checks the CLI, parses a stored output stream, and reads how a CLI that ended by itself ended. A scripted fake can stand in for it with no
/// vendor call (AR-37).
/// </summary>
public interface IAgentAdapter
{
    Agent Agent { get; }

    /// <summary>The one version of the CLI this adapter was released with, the pin of <c>deploy/agents.lock</c> (AD-8).</summary>
    string PinnedVersion { get; }

    /// <summary>Whether the adapter can guarantee the hardening of AD-7 in this authentication mode; if not, the mode is unsupported and the reason says why.</summary>
    Support SupportsAuthentication(AuthenticationMode mode);

    /// <summary>Whether the CLI has a value for this Effort. An Effort that is not supported is reported, never replaced by another (FR-24).</summary>
    Support SupportsEffort(Effort effort);

    /// <exception cref="UnsupportedInvocationException">The mode or the Effort is not supported.</exception>
    AgentInvocation DescribeInvocation(InvocationRequest request);

    /// <summary>An invocation that checks the CLI is present and authenticated, and has capacity; run with no Task volume (AD-20). What its output says is read with <see cref="Parse"/> and <see cref="ClassifyExit"/>.</summary>
    /// <exception cref="UnsupportedInvocationException">The mode is not supported.</exception>
    AgentInvocation DescribeProbe(AuthenticationMode mode);

    /// <summary>Reads a stored output stream line by line. A line that is not an event of the stream is counted and skipped.</summary>
    AgentOutput Parse(IEnumerable<string> lines);

    /// <summary>
    /// What the stream says about a CLI that ended by itself with this exit code: a class only when the stream positively identifies it, <see cref="FailureClass.Unknown"/> when the
    /// run failed and it does not, and null when the exit code is 0 and the stream's result is not an error (AD-11). A non-zero exit after a result that ended well is <see cref="FailureClass.Unknown"/>.
    /// </summary>
    FailureClass? ClassifyExit(int exitCode, AgentOutput output);
}
