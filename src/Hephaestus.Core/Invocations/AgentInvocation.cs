using Hephaestus.Core.Aliases;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Invocations;

/// <summary>What an adapter is asked to describe an invocation of its Agent for (AD-10).</summary>
/// <param name="StepRunId">The step run the invocation is for.</param>
/// <param name="InstructionPath">Where the runtime places the step's instruction, under <see cref="StepContainerLayout.Context"/>. The prompt is <see cref="AgentPrompt.Sentence"/> and names <see cref="AgentPrompt.InstructionPath"/>, so the runtime places the file there.</param>
/// <param name="Authentication">How the Agent signs in; only the secret reference and the hosts differ between modes.</param>
/// <param name="Model">The Model the Alias resolved to, opaque to Hephaestus (<see cref="AliasShape.IsValidModel"/>).</param>
/// <param name="Effort">The Effort Core resolved, or null when none was asked for, which leaves the CLI's own default.</param>
/// <param name="ResultSchema">The JSON schema of the result document Core generated for the step kind (AD-13), or null for a step with no document; the adapter adapts it to its CLI.</param>
public sealed record InvocationRequest(
    Guid StepRunId,
    string InstructionPath,
    AuthenticationMode Authentication,
    string Model,
    Effort? Effort = null,
    string? ResultSchema = null);

/// <summary>
/// An invocation as an adapter describes it (AD-10, AR-19): a fixed record, never a started process. The runtime turns it into a step
/// container and resolves its secret references.
/// </summary>
/// <param name="Command">The argument vector, whole: the first element is the executable.</param>
/// <param name="Environment">The environment, besides <c>HOME</c>; it holds no secret value.</param>
/// <param name="SecretReferences">The secrets the invocation needs, by name and target.</param>
/// <param name="Files">The files the invocation needs from Hephaestus, placed read-only in the context directory.</param>
/// <param name="EgressHosts">The hosts the Agent's CLI needs, which only a container that runs this invocation may reach (AD-7).</param>
/// <param name="Result">Where the invocation leaves its result.</param>
public sealed record AgentInvocation(
    IReadOnlyList<string> Command,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<SecretReference> SecretReferences,
    IReadOnlyList<WorkspaceFile> Files,
    IReadOnlyList<string> EgressHosts,
    ResultLocation Result);
