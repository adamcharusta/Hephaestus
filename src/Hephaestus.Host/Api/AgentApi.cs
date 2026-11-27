using Hephaestus.Core.Aliases;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Readiness;
using Hephaestus.Core.Secrets;

namespace Hephaestus.Host.Api;

/// <summary>
/// The endpoints of how each Agent signs in and whether it is ready (Story 3.10), on the control socket: choosing a mode takes the credential in the body of the PUT, never in an address, and no document
/// the API answers contains it; a check runs the adapter's probe in a container and records what it found.
/// </summary>
internal static class AgentApi
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var agents = routes.MapGroup("/api/agents");
        agents.MapGet(string.Empty, ListAsync);

        // The mode is a query value, so that whatever the Owner typed reaches Core as typed. The body is the credential, which may be empty: the credential stored already is kept.
        agents.MapPut("/{agent}/authentication", SetAsync);
        agents.MapPost("/{agent}/check", CheckAsync);
    }

    private static async Task<IResult> ListAsync(AgentSignInService service, CancellationToken cancellationToken) =>
        Results.Json(new AgentListDocument([.. (await service.ListAsync(cancellationToken)).Select(Documents.Agent)]), ApiJson.Default.AgentListDocument);

    private static async Task<IResult> SetAsync(string agent, string? mode, HttpRequest request, AgentSignInService service, CancellationToken cancellationToken)
    {
        var credential = await ControlApi.ReadValueAsync(request.Body, cancellationToken);
        return Answer(await service.SetAuthenticationAsync(agent, mode, credential, cancellationToken), agent, mode);
    }

    private static async Task<IResult> CheckAsync(string agent, AgentSignInService service, CancellationToken cancellationToken) =>
        Answer(await service.CheckAsync(agent, cancellationToken), agent, null);

    private static IResult Answer(AgentSignInResult result, string agent, string? mode) => result switch
    {
        { Failure: AgentSignInFailure.None, Status: { } status } => Results.Json(Documents.Agent(status), ApiJson.Default.AgentDocument),
        { Failure: AgentSignInFailure.UnknownAgent } => new ProblemResult(StatusCodes.Status422UnprocessableEntity, "agent-unknown", MessageIds.ProblemAgentUnknownTitle, new Message(MessageIds.ProblemAgentUnknownDetail, agent, string.Join(", ", Core.Aliases.Agents.Tags))),
        { Failure: AgentSignInFailure.UnknownMode } => new ProblemResult(StatusCodes.Status422UnprocessableEntity, "agent-mode-unknown", MessageIds.ProblemAgentModeUnknownTitle, new Message(MessageIds.ProblemAgentModeUnknownDetail, mode ?? string.Empty, string.Join(", ", AuthenticationModes.Tags))),
        { Failure: AgentSignInFailure.NoAdapter, Detail: { } detail } => new ProblemResult(StatusCodes.Status422UnprocessableEntity, "agent-no-adapter", MessageIds.ProblemAgentNoAdapterTitle, detail),
        { Failure: AgentSignInFailure.UnsupportedMode, Detail: { } detail } => new ProblemResult(StatusCodes.Status422UnprocessableEntity, "agent-mode-unsupported", MessageIds.ProblemAgentModeUnsupportedTitle, detail),
        { Failure: AgentSignInFailure.CredentialRequired, Detail: { } detail } => new ProblemResult(StatusCodes.Status400BadRequest, "agent-credential-required", MessageIds.ProblemAgentCredentialRequiredTitle, detail),
        { Failure: AgentSignInFailure.CredentialRefused, Refusal: var refusal } => ControlApi.Refuse(refusal) ?? Unexpected,
        { Failure: AgentSignInFailure.NotConfigured, Detail: { } detail } => new ProblemResult(StatusCodes.Status409Conflict, "agent-not-configured", MessageIds.ProblemAgentNotConfiguredTitle, detail),
        { Failure: AgentSignInFailure.ProbeNotRun, Detail: { } detail } => new ProblemResult(StatusCodes.Status503ServiceUnavailable, "agent-probe-not-run", MessageIds.ProblemAgentProbeNotRunTitle, detail),
        _ => Unexpected,
    };

    private static ProblemResult Unexpected => new(StatusCodes.Status500InternalServerError, "unexpected", MessageIds.ProblemUnexpectedTitle, new Message(MessageIds.ProblemUnexpectedDetail));
}
