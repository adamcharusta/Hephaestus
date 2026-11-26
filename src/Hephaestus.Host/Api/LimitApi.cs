using Hephaestus.Core.Limits;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;

namespace Hephaestus.Host.Api;

/// <summary>The endpoints of the installation limits (Story 1.9), on the control socket. A request names the limits to change and no others.</summary>
internal static class LimitApi
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var limits = routes.MapGroup("/api/limits");
        limits.MapGet(string.Empty, GetAsync);
        limits.MapPut(string.Empty, SetAsync);
    }

    private static async Task<IResult> GetAsync(IReadStore store, CancellationToken cancellationToken) =>
        Results.Json(Documents.Limits(await store.GetLimitsAsync(cancellationToken)), ApiJson.Default.LimitsDocument);

    private static async Task<IResult> SetAsync(HttpRequest request, LimitService service, CancellationToken cancellationToken)
    {
        if (await ApiBody.ReadJsonAsync(request, ApiJson.Default.LimitsRequest, cancellationToken) is not { } body)
        {
            return ApiBody.Invalid();
        }

        var result = await service.SetAsync(body.TaskDuration, body.Cpus, body.Memory, cancellationToken);
        return result.Limits is { } limits ? Results.Json(Documents.Limits(limits), ApiJson.Default.LimitsDocument) : Problem(result.Failure, body);
    }

    private static ProblemResult Problem(LimitFailure failure, LimitsRequest body) => failure switch
    {
        LimitFailure.InvalidTaskDuration => new(StatusCodes.Status422UnprocessableEntity, "limit-task-duration-invalid", MessageIds.ProblemLimitTaskDurationInvalidTitle, new Message(MessageIds.ProblemLimitTaskDurationInvalidDetail, body.TaskDuration ?? string.Empty)),
        LimitFailure.InvalidCpus => new(StatusCodes.Status422UnprocessableEntity, "limit-cpus-invalid", MessageIds.ProblemLimitCpusInvalidTitle, new Message(MessageIds.ProblemLimitCpusInvalidDetail, body.Cpus ?? string.Empty)),
        LimitFailure.InvalidMemory => new(StatusCodes.Status422UnprocessableEntity, "limit-memory-invalid", MessageIds.ProblemLimitMemoryInvalidTitle, new Message(MessageIds.ProblemLimitMemoryInvalidDetail, body.Memory ?? string.Empty)),
        _ => new(StatusCodes.Status400BadRequest, "limit-nothing-given", MessageIds.ProblemLimitNothingGivenTitle, new Message(MessageIds.ProblemLimitNothingGivenDetail)),
    };
}
