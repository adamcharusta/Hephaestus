using System.Net;
using System.Text.Json;
using Hephaestus.Core.Localization;

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
    public static Task WriteAsync(HttpContext context, HostSettings settings, int status, string slug, string titleId, Message detail)
    {
        var language = LanguageResolver.ForRequest(context.Request.Headers.AcceptLanguage.ToString(), settings.Language);
        var catalog = MessageCatalog.Instance;
        var problem = new ProblemDocument(TypePrefix + slug, catalog.Resolve(titleId, language), status, catalog.Resolve(detail, language));

        context.Response.StatusCode = status;
        context.Response.ContentType = ContentType;
        return JsonSerializer.SerializeAsync(context.Response.Body, problem, ApiJson.Default.ProblemDocument, context.RequestAborted);
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
