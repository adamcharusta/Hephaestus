using Hephaestus.Core.Invocations;
using Hephaestus.Core.Aliases;

namespace Hephaestus.Core.Ports;

/// <summary>
/// The pure adapter of one Agent (AD-10, AR-19): it describes, and starts nothing and knows nothing of Docker. Story 3.3 defines the half that
/// the end-to-end harness needs, the description of an invocation, so that a scripted fake can stand in for an Agent with no vendor call
/// (AR-37); the probe, the parsing of a stored output stream, the result, and the classification of a CLI that ended by itself join it with
/// Stories 3.8, 3.9, and 3.11.
/// </summary>
public interface IAgentAdapter
{
    Agent Agent { get; }

    AgentInvocation DescribeInvocation(InvocationRequest request);
}
