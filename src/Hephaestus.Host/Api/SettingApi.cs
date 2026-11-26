using Hephaestus.Core.Localization;
using Hephaestus.Core.Settings;

namespace Hephaestus.Host.Api;

/// <summary>The endpoints of the installation settings (Story 1.9), on the control socket. The value of a setting is the plain-text body of the request.</summary>
internal static class SettingApi
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var settings = routes.MapGroup("/api/settings");
        settings.MapGet(string.Empty, ListAsync);
        settings.MapPut("/{name}", SetAsync);
    }

    private static async Task<IResult> ListAsync(SettingService service, CancellationToken cancellationToken)
    {
        var settings = await service.ListAsync(cancellationToken);
        return Results.Json(new SettingListDocument([.. settings.Select(Documents.Setting)]), ApiJson.Default.SettingListDocument);
    }

    private static async Task<IResult> SetAsync(string name, HttpRequest request, SettingService service, CancellationToken cancellationToken)
    {
        var value = await ApiBody.ReadTextAsync(request, cancellationToken);
        var result = await service.SetAsync(name, value, cancellationToken);
        return result.Setting is { } setting ? Results.Json(Documents.Setting(setting), ApiJson.Default.SettingDocument) : Problem(result.Failure, name, value);
    }

    private static ProblemResult Problem(SettingFailure failure, string name, string value) => failure switch
    {
        SettingFailure.InvalidValue => new(StatusCodes.Status422UnprocessableEntity, "setting-value-invalid", MessageIds.ProblemSettingValueInvalidTitle, new Message(MessageIds.ProblemSettingValueInvalidDetail, name, value, InstallationSettings.Find(name)?.Accepts ?? string.Empty)),
        _ => new(StatusCodes.Status404NotFound, "setting-unknown", MessageIds.ProblemSettingUnknownTitle, new Message(MessageIds.ProblemSettingUnknownDetail, name)),
    };
}
