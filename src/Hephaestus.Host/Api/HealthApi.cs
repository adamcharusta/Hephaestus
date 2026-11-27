using Hephaestus.Core.Loop;

namespace Hephaestus.Host.Api;

/// <summary>The health endpoint (AR-42): the orchestrator loop is alive, and when it last ticked.</summary>
internal static class HealthApi
{
    public static void Map(IEndpointRouteBuilder routes) => routes.MapGet("/api/health", Health);

    private static IResult Health(LoopHeartbeat heartbeat)
    {
        var health = heartbeat.Check();
        return Results.Json(new HealthDocument(new LoopHealthDocument(health.Alive, health.LastTickAt is { } tick ? Documents.Time(tick) : null)), ApiJson.Default.HealthDocument);
    }
}
