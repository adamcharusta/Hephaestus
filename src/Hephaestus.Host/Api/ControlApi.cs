using System.Text;
using Hephaestus.Core;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Secrets;

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

        HealthApi.Map(routes);

        // The value of a secret travels in the body of a PUT, never in an address, and no document the API answers contains it.
        var secrets = routes.MapGroup("/api/secrets");
        secrets.MapGet(string.Empty, ListSecretsAsync);
        secrets.MapPut("/{name}", SetSecretAsync);
        secrets.MapDelete("/{name}", RemoveSecretAsync);

        GitHubApi.Map(routes);
        TrustedUserApi.Map(routes);
        AliasApi.Map(routes);
        LimitApi.Map(routes);
        ProtectedPathApi.Map(routes);
        SettingApi.Map(routes);
        ProjectApi.Map(routes);
        ConfigurationApi.Map(routes);
    }

    private static async Task<IResult> ListSecretsAsync(IReadStore store, CancellationToken cancellationToken)
    {
        var names = await store.ListSecretNamesAsync(cancellationToken);
        return Results.Json(new SecretListDocument([.. names.Select(name => new SecretNameDocument(name))]), ApiJson.Default.SecretListDocument);
    }

    private static async Task<IResult> SetSecretAsync(string name, HttpRequest request, ISecretStore store, CancellationToken cancellationToken)
    {
        if (!SecretPolicy.IsValidName(name))
        {
            return InvalidName();
        }

        var value = await ReadValueAsync(request.Body, cancellationToken);
        if (RefuseValue(value) is { } refusal)
        {
            return refusal;
        }

        await store.SetAsync(name, value, cancellationToken);
        return Results.Json(new SecretNameDocument(name), ApiJson.Default.SecretNameDocument);
    }

    private static async Task<IResult> RemoveSecretAsync(string name, ISecretStore store, CancellationToken cancellationToken)
    {
        if (!SecretPolicy.IsValidName(name))
        {
            return InvalidName();
        }

        return await store.RemoveAsync(name, cancellationToken)
            ? Results.Json(new SecretNameDocument(name), ApiJson.Default.SecretNameDocument)
            : new ProblemResult(StatusCodes.Status404NotFound, "secret-not-found", MessageIds.ProblemSecretNotFoundTitle, new Message(MessageIds.ProblemSecretNotFoundDetail, name));
    }

    /// <summary>The problem to answer when a secret value is not acceptable (<see cref="SecretPolicy.CheckValue"/>), or null when it is.</summary>
    internal static IResult? RefuseValue(string value) => SecretPolicy.CheckValue(value) switch
    {
        SecretValueRefusal.Empty => new ProblemResult(StatusCodes.Status400BadRequest, "secret-value-empty", MessageIds.ProblemSecretValueEmptyTitle, new Message(MessageIds.ProblemSecretValueEmptyDetail)),
        SecretValueRefusal.TooShort => new ProblemResult(StatusCodes.Status400BadRequest, "secret-value-too-short", MessageIds.ProblemSecretValueTooShortTitle, new Message(MessageIds.ProblemSecretValueTooShortDetail, SecretPolicy.MinimumValueLength)),
        SecretValueRefusal.TooLong => new ProblemResult(StatusCodes.Status400BadRequest, "secret-value-too-long", MessageIds.ProblemSecretValueTooLongTitle, new Message(MessageIds.ProblemSecretValueTooLongDetail, SecretPolicy.MaximumValueLength)),
        SecretValueRefusal.NotText => new ProblemResult(StatusCodes.Status400BadRequest, "secret-value-not-text", MessageIds.ProblemSecretValueNotTextTitle, new Message(MessageIds.ProblemSecretValueNotTextDetail)),
        _ => null,
    };

    private static ProblemResult InvalidName() =>
        new(StatusCodes.Status400BadRequest, "secret-name-invalid", MessageIds.ProblemSecretNameInvalidTitle, new Message(MessageIds.ProblemSecretNameInvalidDetail, SecretPolicy.MaximumNameLength));

    /// <summary>Reads at most one character more than a value may have, so that an endless body costs a bounded amount of memory.</summary>
    internal static async Task<string> ReadValueAsync(Stream body, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: false);
        var buffer = new char[SecretPolicy.MaximumValueLength + 1];
        var count = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
        return new string(buffer, 0, count);
    }
}
