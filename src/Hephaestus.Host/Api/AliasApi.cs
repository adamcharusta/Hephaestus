using Hephaestus.Core.Aliases;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;

namespace Hephaestus.Host.Api;

/// <summary>
/// The endpoints of the Aliases (Story 1.9), on the control socket. An Alias is added with a JSON body that names an Agent and a
/// Model, or an Effort; Core decides what is acceptable (AD-10).
/// </summary>
internal static class AliasApi
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var aliases = routes.MapGroup("/api/aliases");
        aliases.MapGet(string.Empty, ListAsync);
        aliases.MapPut("/{name}", AddAsync);
        aliases.MapDelete("/{name}", RemoveAsync);
    }

    private static async Task<IResult> ListAsync(IReadStore store, CancellationToken cancellationToken)
    {
        var aliases = await store.ListAliasesAsync(cancellationToken);
        return Results.Json(new AliasListDocument([.. aliases.Select(Documents.Alias)]), ApiJson.Default.AliasListDocument);
    }

    private static async Task<IResult> AddAsync(string name, HttpRequest request, AliasService service, CancellationToken cancellationToken)
    {
        if (await ApiBody.ReadJsonAsync(request, ApiJson.Default.AliasRequest, cancellationToken) is not { } body)
        {
            return ApiBody.Invalid();
        }

        var result = await service.AddAsync(name, body.Agent, body.Model, body.Effort, cancellationToken);
        return result.Alias is { } alias
            ? Results.Json(Documents.Alias(alias), ApiJson.Default.AliasDocument, statusCode: result.Changed ? StatusCodes.Status201Created : StatusCodes.Status200OK)
            : Problem(result.Failure, name, body);
    }

    private static async Task<IResult> RemoveAsync(string name, AliasService service, CancellationToken cancellationToken)
    {
        var result = await service.RemoveAsync(name, cancellationToken);
        return result.Alias is { } alias ? Results.Json(Documents.Alias(alias), ApiJson.Default.AliasDocument) : Problem(result.Failure, name, new AliasRequest(null, null, null));
    }

    private static ProblemResult Problem(AliasFailure failure, string name, AliasRequest body) => failure switch
    {
        AliasFailure.InvalidName => new(StatusCodes.Status400BadRequest, "alias-name-invalid", MessageIds.ProblemAliasNameInvalidTitle, new Message(MessageIds.ProblemAliasNameInvalidDetail, AliasShape.MaximumNameLength)),
        AliasFailure.Incomplete => new(StatusCodes.Status400BadRequest, "alias-incomplete", MessageIds.ProblemAliasIncompleteTitle, new Message(MessageIds.ProblemAliasIncompleteDetail)),
        AliasFailure.UnknownAgent => new(StatusCodes.Status422UnprocessableEntity, "alias-agent-unknown", MessageIds.ProblemAliasAgentUnknownTitle, new Message(MessageIds.ProblemAliasAgentUnknownDetail, body.Agent ?? string.Empty, string.Join(", ", Agents.Tags))),
        AliasFailure.InvalidModel => new(StatusCodes.Status400BadRequest, "alias-model-invalid", MessageIds.ProblemAliasModelInvalidTitle, new Message(MessageIds.ProblemAliasModelInvalidDetail, AliasShape.MaximumModelLength)),
        AliasFailure.UnknownEffort => new(StatusCodes.Status422UnprocessableEntity, "alias-effort-unknown", MessageIds.ProblemAliasEffortUnknownTitle, new Message(MessageIds.ProblemAliasEffortUnknownDetail, body.Effort ?? string.Empty, string.Join(", ", Efforts.Tags))),
        AliasFailure.DefinedDifferently => new(StatusCodes.Status409Conflict, "alias-defined-differently", MessageIds.ProblemAliasDefinedDifferentlyTitle, new Message(MessageIds.ProblemAliasDefinedDifferentlyDetail, name)),
        _ => new(StatusCodes.Status404NotFound, "alias-not-found", MessageIds.ProblemAliasNotFoundTitle, new Message(MessageIds.ProblemAliasNotFoundDetail, name)),
    };
}
