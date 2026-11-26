using System.Net;
using System.Text.Json;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Settings;

namespace Hephaestus.Host.Api;

/// <summary>
/// Writes every failed request as an RFC 9457 problem document. The handler names a message id; Core resolves it in the
/// language of the request (AD-21). No sentence is composed here.
/// </summary>
internal static class ProblemResponses
{
    public const string ContentType = "application/problem+json";
    private const string TypePrefix = "urn:hephaestus:problem:";

    public static Task WriteAsync(HttpContext context, HostSettings settings)
    {
        var status = context.Response.StatusCode;
        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method;

        var (slug, title, detail) = status switch
        {
            StatusCodes.Status404NotFound => ("not-found", MessageIds.ProblemNotFoundTitle, new Message(MessageIds.ProblemNotFoundDetail, path)),
            StatusCodes.Status405MethodNotAllowed => ("method-not-allowed", MessageIds.ProblemMethodNotAllowedTitle, new Message(MessageIds.ProblemMethodNotAllowedDetail, method, path)),
            StatusCodes.Status500InternalServerError => ("unexpected", MessageIds.ProblemUnexpectedTitle, new Message(MessageIds.ProblemUnexpectedDetail)),
            _ => ("failed", MessageIds.ProblemFailedTitle, new Message(MessageIds.ProblemFailedDetail, status)),
        };

        return WriteAsync(context, settings, status, slug, title, detail);
    }

    /// <summary>Writes a problem the handler chose: its status, the <c>type</c> slug, the title id, and the detail message.</summary>
    public static async Task WriteAsync(HttpContext context, HostSettings settings, int status, string slug, string titleId, Message detail)
    {
        var language = LanguageResolver.FromHeader(context.Request.Headers.AcceptLanguage.ToString()) ?? await InstallationLanguageAsync(context, settings);
        var catalog = MessageCatalog.Instance;
        var problem = new ProblemDocument(TypePrefix + slug, catalog.Resolve(titleId, language), status, catalog.Resolve(detail, language));

        context.Response.StatusCode = status;
        context.Response.ContentType = ContentType;
        await JsonSerializer.SerializeAsync(context.Response.Body, problem, ApiJson.Default.ProblemDocument, context.RequestAborted);
    }

    /// <summary>
    /// The language the Owner stored; the environment's when none was stored, or when the database cannot be read, because a failure
    /// about the database is still to be told.
    /// </summary>
    private static async Task<Language> InstallationLanguageAsync(HttpContext context, HostSettings settings)
    {
        try
        {
            var stored = await context.RequestServices.GetRequiredService<IReadStore>().ListSettingsAsync(context.RequestAborted);
            return LanguageResolver.Installation(InstallationSettings.StoredLanguage(stored), settings.Language);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return settings.Language;
        }
    }

    public static Task WriteUnexpectedAsync(HttpContext context, HostSettings settings)
    {
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        return WriteAsync(context, settings);
    }
}

/// <summary>A problem document as the result of an endpoint.</summary>
internal sealed class ProblemResult(int status, string slug, string titleId, Message detail) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext) =>
        ProblemResponses.WriteAsync(httpContext, httpContext.RequestServices.GetRequiredService<HostSettings>(), status, slug, titleId, detail);
}
