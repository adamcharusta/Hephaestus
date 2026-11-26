using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Protection;

namespace Hephaestus.Host.Api;

/// <summary>
/// The endpoints of the protected paths (Story 1.9, AD-19), on the control socket. A glob holds slashes and asterisks, so it travels
/// in the <c>pattern</c> query value, not in the path.
/// </summary>
internal static class ProtectedPathApi
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var paths = routes.MapGroup("/api/protected-paths");
        paths.MapGet(string.Empty, ListAsync);
        paths.MapPut(string.Empty, AddAsync);
        paths.MapDelete(string.Empty, RemoveAsync);
    }

    private static async Task<IResult> ListAsync(IReadStore store, CancellationToken cancellationToken)
    {
        var paths = await store.ListProtectedPathsAsync(cancellationToken);
        return Results.Json(new ProtectedPathListDocument([.. paths]), ApiJson.Default.ProtectedPathListDocument);
    }

    private static async Task<IResult> AddAsync(string? pattern, ProtectedPathService service, CancellationToken cancellationToken)
    {
        var result = await service.AddAsync(pattern ?? string.Empty, cancellationToken);
        return result.Glob is { } glob
            ? Results.Json(new ProtectedPathDocument(glob), ApiJson.Default.ProtectedPathDocument, statusCode: result.Changed ? StatusCodes.Status201Created : StatusCodes.Status200OK)
            : Problem(result.Failure, pattern);
    }

    private static async Task<IResult> RemoveAsync(string? pattern, ProtectedPathService service, CancellationToken cancellationToken)
    {
        var result = await service.RemoveAsync(pattern ?? string.Empty, cancellationToken);
        return result.Glob is { } glob ? Results.Json(new ProtectedPathDocument(glob), ApiJson.Default.ProtectedPathDocument) : Problem(result.Failure, pattern);
    }

    private static ProblemResult Problem(ProtectedPathFailure failure, string? pattern) => failure switch
    {
        ProtectedPathFailure.InvalidGlob => new(StatusCodes.Status400BadRequest, "protected-path-invalid", MessageIds.ProblemProtectedPathInvalidTitle, new Message(MessageIds.ProblemProtectedPathInvalidDetail, ProtectedPath.MaximumLength)),
        _ => new(StatusCodes.Status404NotFound, "protected-path-not-found", MessageIds.ProblemProtectedPathNotFoundTitle, new Message(MessageIds.ProblemProtectedPathNotFoundDetail, pattern ?? string.Empty)),
    };
}
