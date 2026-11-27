using System.Globalization;
using System.Text.Json;
using Hephaestus.Core.Invocations;

namespace Hephaestus.Agents;

/// <summary>
/// Reads the stream of <c>claude -p --output-format stream-json --verbose</c> (AD-10) into the normalized events and result. It reads only what the CLI
/// printed, skips what it does not know, and never throws on a line.
/// </summary>
internal static class ClaudeCodeStream
{
    /// <summary>The error the CLI names when its credential is missing or refused; the stream of a run with no credential and with a wrong one both carry it.</summary>
    public const string AuthenticationFailed = "authentication_failed";

    /// <summary>The start of the subtype of a result the CLI ended in an error of its own: <c>error_max_turns</c>, <c>error_during_execution</c>.</summary>
    public const string ErrorSubtypePrefix = "error_";

    public static AgentOutput Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var events = new List<AgentEvent>();
        var limits = new List<AgentRateLimit>();
        var toolNames = new Dictionary<string, string>(StringComparer.Ordinal);
        AgentResult? result = null;
        string? model = null;
        string? lastError = null;
        var unreadable = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                // A lone surrogate in the line is an ArgumentException of the string overload, not a JsonException.
                unreadable++;
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || String(root, "type") is not { } type)
                {
                    unreadable++;
                    continue;
                }

                switch (type)
                {
                    case "system" when String(root, "subtype") == "init":
                        model = String(root, "model");
                        events.Add(new AgentEvent(AgentEventKind.Started, null, model, false, null));
                        break;
                    case "assistant":
                        lastError = ReadAssistant(root, events, toolNames);
                        break;
                    case "user":
                        ReadUser(root, events, toolNames);
                        break;
                    case "rate_limit_event" when root.TryGetProperty("rate_limit_info", out var info) && info.ValueKind == JsonValueKind.Object:
                        var limit = new AgentRateLimit(String(info, "rateLimitType") ?? "unknown", String(info, "status") ?? "unknown", Instant(info, "resetsAt"));
                        limits.Add(limit);
                        events.Add(new AgentEvent(AgentEventKind.RateLimit, limit.Type, limit.Status, false, null));
                        break;
                    case "result":
                        result = ReadResult(root, model, lastError);
                        break;
                }
            }
        }

        return new AgentOutput(events, result, limits, unreadable);
    }

    /// <summary>Reads the content blocks of an assistant message; returns the error the message names, when it names one. A later message that names none leaves a result with no error of its own to blame.</summary>
    private static string? ReadAssistant(JsonElement root, List<AgentEvent> events, Dictionary<string, string> toolNames)
    {
        var error = String(root, "error");
        var at = Timestamp(root);
        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return error;
        }

        foreach (var block in content.EnumerateArray())
        {
            switch (String(block, "type"))
            {
                case "text":
                    events.Add(new AgentEvent(AgentEventKind.Message, null, Cut(String(block, "text")), error is not null, at));
                    break;
                case "tool_use":
                    var name = String(block, "name");
                    if (String(block, "id") is { } id && name is not null)
                    {
                        toolNames[id] = name;
                    }

                    events.Add(new AgentEvent(AgentEventKind.ToolCall, name, block.TryGetProperty("input", out var input) ? Cut(input.GetRawText()) : null, false, at));
                    break;
            }
        }

        return error;
    }

    private static void ReadUser(JsonElement root, List<AgentEvent> events, Dictionary<string, string> toolNames)
    {
        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var at = Timestamp(root);
        foreach (var block in content.EnumerateArray().Where(block => String(block, "type") == "tool_result"))
        {
            var name = String(block, "tool_use_id") is { } id && toolNames.TryGetValue(id, out var known) ? known : null;
            var isError = block.TryGetProperty("is_error", out var flag) && flag.ValueKind == JsonValueKind.True;
            events.Add(new AgentEvent(AgentEventKind.ToolResult, name, Cut(ToolText(block)), isError, at));
        }
    }

    private static string? ToolText(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var content))
        {
            return null;
        }

        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString(),
            JsonValueKind.Array => string.Join('\n', content.EnumerateArray().Select(part => String(part, "text")).Where(text => text is not null)),
            _ => null,
        };
    }

    private static AgentResult ReadResult(JsonElement root, string? initModel, string? assistantError)
    {
        var subtype = String(root, "subtype");
        var errorSubtype = subtype is not null && subtype.StartsWith(ErrorSubtypePrefix, StringComparison.Ordinal);
        var isError = errorSubtype || root.TryGetProperty("is_error", out var flag) && flag.ValueKind == JsonValueKind.True;
        var structured = root.TryGetProperty("structured_output", out var output) && output.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? output.GetRawText() : null;
        var error = !isError ? null : errorSubtype ? subtype : assistantError;
        return new AgentResult(isError, String(root, "result"), structured, initModel ?? FirstModel(root), ReadUsage(root), error);
    }

    private static string? FirstModel(JsonElement root) =>
        root.TryGetProperty("modelUsage", out var models) && models.ValueKind == JsonValueKind.Object ? models.EnumerateObject().Select(model => model.Name).FirstOrDefault() : null;

    private static AgentUsage? ReadUsage(JsonElement root)
    {
        long? Count(string name) => root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object ? Number(usage, name) : null;
        decimal? cost = root.TryGetProperty("total_cost_usd", out var total) && total.ValueKind == JsonValueKind.Number && total.TryGetDecimal(out var dollars) ? dollars : null;
        var result = new AgentUsage(Count("input_tokens"), Count("output_tokens"), Count("cache_read_input_tokens"), Count("cache_creation_input_tokens"), cost);
        return result == new AgentUsage(null, null, null, null, null) ? null : result;
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;

    private static DateTimeOffset? Instant(JsonElement element, string name) =>
        Number(element, name) is { } seconds and >= 0 and <= 253_402_300_799 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;

    private static DateTimeOffset? Timestamp(JsonElement element) =>
        String(element, "timestamp") is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at) ? at : null;

    private static string? Cut(string? text)
    {
        if (text is not { Length: > AgentEvent.MaximumTextLength })
        {
            return text;
        }

        // Never end on the first half of a surrogate pair.
        var length = char.IsHighSurrogate(text[AgentEvent.MaximumTextLength - 1]) ? AgentEvent.MaximumTextLength - 1 : AgentEvent.MaximumTextLength;
        return text[..length];
    }
}
