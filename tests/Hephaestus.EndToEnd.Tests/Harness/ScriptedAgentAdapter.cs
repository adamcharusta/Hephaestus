using System.Text;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Failures;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.EndToEnd.Tests.Harness;

/// <summary>
/// The scripted fake <see cref="IAgentAdapter"/> of the end-to-end harness (AR-37, Story 3.3): its invocation runs a shell script in a step container and
/// makes no vendor call, so a test decides what the "Agent" prints, how long it runs, and how it ends. It starts nothing and knows nothing of Docker, as an adapter does not (AD-10).
/// Every later story of the epic adds to the harness what it needs of an adapter.
/// </summary>
internal sealed class ScriptedAgentAdapter(string script, Agent agent = Agent.ClaudeCode) : IAgentAdapter
{
    public const string ResultPath = StepContainerLayout.Workspace + "/result.txt";

    public string Script { get; } = script;

    public Agent Agent { get; } = agent;

    public string PinnedVersion => "scripted";

    public Support SupportsAuthentication(AuthenticationMode mode) => Support.Yes;

    public Support SupportsEffort(Effort effort) => Support.Yes;

    public AgentInvocation DescribeInvocation(InvocationRequest request) => new(
        ["/bin/sh", "-c", Script],
        new Dictionary<string, string>(),
        [],
        [new WorkspaceFile(request.InstructionPath, Encoding.UTF8.GetBytes(Script))],
        [],
        ResultLocation.InFile(ResultPath));

    public AgentInvocation DescribeProbe(AuthenticationMode mode) => new(["/bin/sh", "-c", "exit 0"], new Dictionary<string, string>(), [], [], [], ResultLocation.InOutputStream);

    /// <summary>The line that carries the result of a step: <c>RESULT:</c> and the document, which is how the scripted Agent "answers".</summary>
    public const string ResultPrefix = "RESULT:";

    public AgentOutput Parse(IEnumerable<string> lines)
    {
        var result = lines.FirstOrDefault(line => line.StartsWith(ResultPrefix, StringComparison.Ordinal))?[ResultPrefix.Length..];
        return new AgentOutput([], result is null ? null : new AgentResult(false, null, result, "scripted", null, null), [], 0);
    }

    public FailureClass? ClassifyExit(int exitCode, AgentOutput output) => exitCode == 0 ? null : FailureClass.Unknown;
}
