using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Invocations;

/// <summary>What an adapter is asked to describe an invocation of its Agent for (AD-10).</summary>
/// <param name="StepRunId">The step run the invocation is for.</param>
/// <param name="InstructionPath">Where the step's instruction is, under <see cref="StepContainerLayout.Context"/>.</param>
public sealed record InvocationRequest(Guid StepRunId, string InstructionPath);

/// <summary>
/// An invocation as an adapter describes it (AD-10, AR-19): a fixed record, never a started process. The runtime turns it into a step
/// container. Secret references, which Story 3.10 adds, are not part of it yet.
/// </summary>
/// <param name="Command">The argument vector, whole: the first element is the executable.</param>
/// <param name="Environment">The environment, besides <c>HOME</c>.</param>
/// <param name="Files">The files the invocation needs from Hephaestus, placed read-only in the context directory.</param>
/// <param name="EgressHosts">The hosts the Agent's CLI needs, which only a container that runs this invocation may reach (AD-7).</param>
/// <param name="ResultPath">Where the invocation leaves its result.</param>
public sealed record AgentInvocation(
    IReadOnlyList<string> Command,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<WorkspaceFile> Files,
    IReadOnlyList<string> EgressHosts,
    string ResultPath);
