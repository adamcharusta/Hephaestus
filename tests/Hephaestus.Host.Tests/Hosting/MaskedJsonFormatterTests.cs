using System.Text.Json;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Secrets;
using Hephaestus.Host.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Hephaestus.Host.Tests.Hosting;

/// <summary>Story 1.6, FR-4, AD-16: the line the logger writes holds a mask where a stored secret was, in every form and in every field.</summary>
public class MaskedJsonFormatterTests
{
    private const string Value = "sk-test-0123456789-abcdefghij";
    private const string Mask = SecretMasker.Replacement;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Line(RunningHost host, Action<ILogger> log)
    {
        var formatter = host.App.Services.GetServices<ConsoleFormatter>().OfType<MaskedJsonFormatter>().Single();
        var scopes = new LoggerExternalScopeProvider();
        var writer = new StringWriter();
        log(new FormattingLogger(formatter, scopes, writer));
        return writer.ToString();
    }

    private static async Task StoreAsync(RunningHost host, string value) =>
        await host.App.Services.GetRequiredService<ISecretStore>().SetAsync("token", value, Token);

    [Fact]
    public async Task A_stored_value_in_plain_base64_or_url_encoded_form_is_a_mask_in_the_line_written()
    {
        await using var host = await RunningHost.StartAsync();
        await StoreAsync(host, Value);
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Value));

        var plain = Line(host, logger => logger.LogInformation("curl -H 'Authorization: {Header}'", "Bearer " + Value));
        var encoded = Line(host, logger => logger.LogInformation("echo {Value} | base64 -d", base64));
        var escaped = Line(host, logger => logger.LogWarning("GET /?key={Key}", Uri.EscapeDataString(Value + " &=")));

        Assert.Contains($"Bearer {Mask}", plain, StringComparison.Ordinal);
        Assert.Contains($"echo {Mask} | base64", encoded, StringComparison.Ordinal);
        Assert.Contains($"key={Mask}", escaped, StringComparison.Ordinal);
        foreach (var line in new[] { plain, encoded, escaped })
        {
            Assert.DoesNotContain("0123456789", line, StringComparison.Ordinal);
            Assert.DoesNotContain(base64[..16], line, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(line);
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        }
    }

    [Fact]
    public async Task A_value_that_json_would_escape_is_masked_before_it_is_encoded()
    {
        await using var host = await RunningHost.StartAsync();
        const string Awkward = "pa\"ss\\word+zażółć\tend";
        await StoreAsync(host, Awkward);

        var line = Line(host, logger => logger.LogError("password is {Password}", Awkward));

        using var document = JsonDocument.Parse(line);
        Assert.Equal($"password is {Mask}", document.RootElement.GetProperty("Message").GetString());
        Assert.Equal(Mask, document.RootElement.GetProperty("State").GetProperty("Password").GetString());
        Assert.DoesNotContain("zażółć", line, StringComparison.Ordinal);
        Assert.DoesNotContain("za\\u017C", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_exception_text_and_the_scopes_are_masked_too()
    {
        await using var host = await RunningHost.StartAsync();
        await StoreAsync(host, Value);
        var scopes = new LoggerExternalScopeProvider();
        var formatter = host.App.Services.GetServices<ConsoleFormatter>().OfType<MaskedJsonFormatter>().Single();
        var writer = new StringWriter();
        var logger = new FormattingLogger(formatter, scopes, writer);

        using (scopes.Push($"plain {Value}"))
        using (scopes.Push(new Dictionary<string, object?> { ["taskId"] = Value, ["count"] = 3 }))
        {
            logger.LogError(new InvalidOperationException($"the key {Value} was rejected"), "Step failed");
        }

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Contains($"the key {Mask} was rejected", root.GetProperty("Exception").GetString(), StringComparison.Ordinal);
        var written = root.GetProperty("Scopes");
        Assert.Equal($"plain {Mask}", written[0].GetString());
        Assert.Equal(Mask, written[1].GetProperty("taskId").GetString());
        Assert.Equal(3, written[1].GetProperty("count").GetInt32());
        Assert.DoesNotContain("0123456789", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Numbers_booleans_and_null_keep_their_json_type_and_the_category_and_keys_are_masked_too()
    {
        await using var host = await RunningHost.StartAsync();
        await StoreAsync(host, Value);

        var line = Line(host, logger => logger.LogInformation("{Count} {Flag} {Nothing} {Big} {Ratio} {Price} {When}", 3, true, null, 5_000_000_000L, 0.5, 1.25m, "2026-11-26"));
        var named = Line(host, logger => logger.LogInformation("{" + "Key" + "}", Value));

        using var document = JsonDocument.Parse(line);
        var state = document.RootElement.GetProperty("State");
        Assert.Equal(3, state.GetProperty("Count").GetInt32());
        Assert.Equal(JsonValueKind.True, state.GetProperty("Flag").ValueKind);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("Nothing").ValueKind);
        Assert.Equal(5_000_000_000L, state.GetProperty("Big").GetInt64());
        Assert.Equal(0.5, state.GetProperty("Ratio").GetDouble());
        Assert.Equal(1.25m, state.GetProperty("Price").GetDecimal());
        Assert.Equal("2026-11-26", state.GetProperty("When").GetString());
        Assert.DoesNotContain(Value, named, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_category_that_holds_a_secret_is_masked()
    {
        await using var host = await RunningHost.StartAsync();
        await StoreAsync(host, Value);
        var formatter = host.App.Services.GetServices<ConsoleFormatter>().OfType<MaskedJsonFormatter>().Single();
        var writer = new StringWriter();

        formatter.Write(new LogEntry<string>(LogLevel.Information, "category-" + Value, new EventId(1), "state", null, (s, _) => s), null, writer);

        Assert.DoesNotContain(Value, writer.ToString(), StringComparison.Ordinal);
        Assert.Contains($"category-{Mask}", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_value_stored_or_removed_while_the_host_runs_is_masked_or_released_on_the_next_line()
    {
        await using var host = await RunningHost.StartAsync();

        var before = Line(host, logger => logger.LogInformation("value {Value}", Value));
        await StoreAsync(host, Value);
        var during = Line(host, logger => logger.LogInformation("value {Value}", Value));
        await host.App.Services.GetRequiredService<ISecretStore>().RemoveAsync("token", Token);
        var after = Line(host, logger => logger.LogInformation("value {Value}", Value));

        Assert.Contains(Value, before, StringComparison.Ordinal);
        Assert.DoesNotContain(Value, during, StringComparison.Ordinal);
        Assert.Contains(Mask, during, StringComparison.Ordinal);
        Assert.Contains(Value, after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Secrets_already_on_the_volume_are_masked_from_the_first_line_after_a_start()
    {
        using var installation = new TempSocket();
        Directory.CreateDirectory(installation.SecretsDirectory);
        await File.WriteAllTextAsync(Path.Combine(installation.SecretsDirectory, "token"), Value, Token);

        await using var host = await RunningHost.StartAsync(installation: installation);
        var line = Line(host, logger => logger.LogInformation("value {Value}", Value));

        Assert.DoesNotContain(Value, line, StringComparison.Ordinal);
        Assert.Contains(Mask, line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_line_without_a_message_or_an_exception_writes_nothing_and_the_timestamp_follows_the_options()
    {
        await using var host = await RunningHost.StartAsync();
        var formatter = host.App.Services.GetServices<ConsoleFormatter>().OfType<MaskedJsonFormatter>().Single();
        var writer = new StringWriter();

        formatter.Write(new LogEntry<string>(LogLevel.Information, "category", new EventId(1), "state", null, null!), null, writer);
        var line = Line(host, logger => logger.LogInformation("hello"));

        Assert.Equal(string.Empty, writer.ToString());
        using var document = JsonDocument.Parse(line);
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z$", document.RootElement.GetProperty("Timestamp").GetString());
        Assert.False(document.RootElement.TryGetProperty("Exception", out _));
    }

    /// <summary>A logger that hands each entry to the formatter, as the console logger does, and keeps what the formatter wrote.</summary>
    private sealed class FormattingLogger(MaskedJsonFormatter formatter, IExternalScopeProvider scopes, TextWriter writer) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatted) =>
            formatter.Write(new LogEntry<TState>(logLevel, "Test", eventId, state, exception, formatted), scopes, writer);
    }
}
