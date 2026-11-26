using Hephaestus.Core;

namespace Hephaestus.Host.Api;

/// <summary>
/// The control API (AD-17). It is mapped on the Unix socket only, and carries every endpoint that changes anything; the
/// dashboard listener of Story 1.14 maps reads alone.
/// </summary>
internal static class ControlApi
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/version", () => Results.Json(new VersionDocument(HephaestusVersion.Current), ApiJson.Default.VersionDocument));
    }
}
