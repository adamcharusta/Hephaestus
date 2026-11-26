using System.Globalization;
using System.Text;
using System.Text.Json;
using Hephaestus.Core.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Hephaestus.Host.Hosting;

/// <summary>Options of <see cref="MaskedJsonFormatter"/>: the console formatter's own (scopes, UTC timestamps, the timestamp format).</summary>
internal sealed class MaskedJsonFormatterOptions : ConsoleFormatterOptions;

/// <summary>
/// The logger's one formatter (AD-16, AR-36): one JSON object per line, in the shape of the framework's JSON console formatter,
/// with every string passed through <see cref="SecretMasker"/> before it is encoded. Masking the text and not the encoded line
/// is what catches a value that JSON would have escaped (a quote, a backslash, a character outside ASCII).
/// </summary>
internal sealed class MaskedJsonFormatter(IOptionsMonitor<MaskedJsonFormatterOptions> options, SecretMasker masker, TimeProvider time)
    : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "hephaestus-json";

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (logEntry.Exception is null && message is null)
        {
            return;
        }

        var settings = options.CurrentValue;
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            json.WriteStartObject();
            json.WriteString("Timestamp", Timestamp(settings));
            json.WriteNumber("EventId", logEntry.EventId.Id);
            json.WriteString("LogLevel", logEntry.LogLevel.ToString());
            json.WriteString("Category", Masked(logEntry.Category));
            json.WriteString("Message", Masked(message));
            if (logEntry.Exception is not null)
            {
                json.WriteString("Exception", Masked(logEntry.Exception.ToString()));
            }

            if (logEntry.State is IReadOnlyList<KeyValuePair<string, object?>> state)
            {
                json.WriteStartObject("State");
                json.WriteString("Message", Masked(logEntry.State.ToString()));
                foreach (var (key, value) in state)
                {
                    WriteValue(json, key, value);
                }

                json.WriteEndObject();
            }

            if (settings.IncludeScopes && scopeProvider is not null)
            {
                json.WriteStartArray("Scopes");
                scopeProvider.ForEachScope(WriteScope, json);
                json.WriteEndArray();
            }

            json.WriteEndObject();
        }

        textWriter.Write(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
        textWriter.Write(Environment.NewLine);
    }

    private void WriteScope(object? scope, Utf8JsonWriter json)
    {
        if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            json.WriteStartObject();
            json.WriteString("Message", Masked(scope.ToString()));
            foreach (var (key, value) in pairs)
            {
                WriteValue(json, key, value);
            }

            json.WriteEndObject();
            return;
        }

        json.WriteStringValue(Masked(Convert.ToString(scope, CultureInfo.InvariantCulture)));
    }

    /// <summary>A number, a boolean, or null keeps its JSON type, as in the framework's formatter; anything else is masked text.</summary>
    private void WriteValue(Utf8JsonWriter json, string key, object? value)
    {
        var name = Masked(key);
        switch (value)
        {
            case null:
                json.WriteNull(name);
                break;
            case bool flag:
                json.WriteBoolean(name, flag);
                break;
            case int number:
                json.WriteNumber(name, number);
                break;
            case long number:
                json.WriteNumber(name, number);
                break;
            case double number when double.IsFinite(number):
                json.WriteNumber(name, number);
                break;
            case decimal number:
                json.WriteNumber(name, number);
                break;
            default:
                json.WriteString(name, Masked(Convert.ToString(value, CultureInfo.InvariantCulture)));
                break;
        }
    }

    private string Timestamp(MaskedJsonFormatterOptions settings)
    {
        var now = settings.UseUtcTimestamp ? time.GetUtcNow() : time.GetLocalNow();
        return now.ToString(settings.TimestampFormat ?? "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }

    private string Masked(string? text) => masker.Mask(text ?? string.Empty);
}
