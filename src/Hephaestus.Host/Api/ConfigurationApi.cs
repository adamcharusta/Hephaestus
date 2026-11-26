using Hephaestus.Core.Ports;
using Hephaestus.Core.Settings;

namespace Hephaestus.Host.Api;

/// <summary>
/// The read of the whole Installation Configuration (Story 1.9, FR-3): Trusted Users, Aliases, limits, protected paths, settings,
/// and the names of the secrets. A secret's value is not read to make it, so it cannot be in it.
/// </summary>
internal static class ConfigurationApi
{
    public static void Map(IEndpointRouteBuilder routes) => routes.MapGet("/api/configuration", ShowAsync);

    private static async Task<IResult> ShowAsync(IReadStore store, SettingService settings, CancellationToken cancellationToken)
    {
        var document = new ConfigurationDocument(
            [.. (await store.ListTrustedUsersAsync(cancellationToken)).Select(Documents.TrustedUser)],
            [.. (await store.ListAliasesAsync(cancellationToken)).Select(Documents.Alias)],
            Documents.Limits(await store.GetLimitsAsync(cancellationToken)),
            [.. await store.ListProtectedPathsAsync(cancellationToken)],
            [.. (await settings.ListAsync(cancellationToken)).Select(Documents.Setting)],
            [.. (await store.ListSecretNamesAsync(cancellationToken)).Select(name => new SecretNameDocument(name))]);
        return Results.Json(document, ApiJson.Default.ConfigurationDocument);
    }
}
