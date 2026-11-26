using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Hephaestus.Host.Api;

/// <summary>Reads the body of a request that changes configuration, never more than <see cref="MaximumLength"/> characters of it.</summary>
internal static class ApiBody
{
    public const int MaximumLength = 4096;

    /// <summary>The body as a document of the shape given, or null when it is not one (empty, too long, or not that JSON).</summary>
    public static async Task<T?> ReadJsonAsync<T>(HttpRequest request, JsonTypeInfo<T> shape, CancellationToken cancellationToken)
        where T : class
    {
        var text = await ReadTextAsync(request, cancellationToken);
        if (text.Length is 0 or > MaximumLength)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(text, shape);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>At most one character more than a document may have, so that an endless body costs a bounded amount of memory.</summary>
    public static async Task<string> ReadTextAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: false);
        var buffer = new char[MaximumLength + 1];
        var count = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
        return new string(buffer, 0, count);
    }

    public static ProblemResult Invalid() =>
        new(StatusCodes.Status400BadRequest, "request-invalid", Core.Localization.MessageIds.ProblemRequestInvalidTitle, new Core.Localization.Message(Core.Localization.MessageIds.ProblemRequestInvalidDetail));
}
