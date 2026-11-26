using System.Text.Json;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;

namespace Hephaestus.Host.Tests.Cli;

/// <summary>Story 1.5 on the command line: <c>secret set | list | remove</c>, the value on standard input and nowhere else.</summary>
public class SecretCommandTests
{
    private const string Value = "sk-test-0123456789-abcdefghij";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task<CliRun> SetAsync(RunningHost host, string name, string input, Language language = Language.English, params string[] more) =>
        CliRun.ExecuteWithInputAsync(input, host.Settings.ControlSocketPath, language, ["secret", "set", name, .. more]);

    private static Task<CliRun> RunAsync(RunningHost host, Language language, params string[] args) =>
        CliRun.ExecuteAsync(host.Settings.ControlSocketPath, language, args);

    private static string FileOf(RunningHost host, string name) => Path.Combine(host.Settings.SecretsDirectory, name);

    [Fact]
    public async Task Set_reads_the_value_from_standard_input_stores_it_and_names_the_secret()
    {
        await using var host = await RunningHost.StartAsync();

        var run = await SetAsync(host, "anthropic-api-key", Value + "\n");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal($"Stored secret anthropic-api-key.{Environment.NewLine}", run.Output);
        Assert.Equal(string.Empty, run.Error);
        Assert.Equal(Value, await File.ReadAllTextAsync(FileOf(host, "anthropic-api-key"), Token));
        Assert.DoesNotContain(Value, run.Output + run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Set_with_json_prints_the_name_in_one_document_and_nothing_else()
    {
        await using var host = await RunningHost.StartAsync();

        var run = await SetAsync(host, "token", Value, Language.English, "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal($$"""{"name":"token"}{{Environment.NewLine}}""", run.Output);
        Assert.Equal(string.Empty, run.Error);
    }

    [Theory]
    [InlineData("\n", "")]
    [InlineData("\r\n", "")]
    [InlineData("", "")]
    [InlineData("\n", "\n")]
    public async Task One_trailing_line_break_of_the_input_is_not_part_of_the_value(string ending, string keptEnding)
    {
        await using var host = await RunningHost.StartAsync();

        await SetAsync(host, "token", Value + keptEnding + ending);

        Assert.Equal(Value + keptEnding, await File.ReadAllTextAsync(FileOf(host, "token"), Token));
    }

    [Fact]
    public async Task A_value_given_as_an_argument_is_refused_without_repeating_it_and_nothing_is_stored()
    {
        await using var host = await RunningHost.StartAsync();

        var argument = await RunAsync(host, Language.English, "secret", "set", "token", Value);
        var option = await RunAsync(host, Language.English, "secret", "set", "--value", Value);
        var equals = await RunAsync(host, Language.English, "secret", "set", $"--value={Value}");
        var second = await RunAsync(host, Language.English, "secret", "set", "token", "--json", Value);

        foreach (var run in new[] { argument, option, equals, second })
        {
            Assert.Equal(2, run.ExitCode);
            Assert.Equal(string.Empty, run.Output);
            Assert.Equal("A secret value cannot be given on the command line", run.ErrorLines[0]);
            Assert.StartsWith("Nothing was stored. Give the value on standard input", run.ErrorLines[1], StringComparison.Ordinal);
            Assert.DoesNotContain(Value, run.Error, StringComparison.Ordinal);
        }

        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListSecretNamesAsync(Token));
        Assert.False(Directory.Exists(host.Settings.SecretsDirectory));
    }

    [Theory]
    [InlineData("--json=VALUEXXX1")]
    [InlineData("--jsno")]
    [InlineData("-x")]
    public async Task Any_other_failed_parse_of_set_names_no_token(string token)
    {
        await using var host = await RunningHost.StartAsync();

        var run = await RunAsync(host, Language.English, "secret", "set", "token", token);
        var named = await RunAsync(host, Language.English, "secret", "set", token);

        Assert.All(new[] { run, named }, r =>
        {
            Assert.Equal(2, r.ExitCode);
            Assert.Equal("A secret value cannot be given on the command line", r.ErrorLines[0]);
            Assert.DoesNotContain(token, r.Error, StringComparison.Ordinal);
        });
        Assert.False(Directory.Exists(host.Settings.SecretsDirectory));
    }

    [Fact]
    public async Task A_value_of_the_longest_length_is_accepted_with_any_line_break_after_it()
    {
        await using var host = await RunningHost.StartAsync();
        var longest = new string('x', 65_536);

        var crlf = await SetAsync(host, "crlf", longest + "\r\n");
        var lf = await SetAsync(host, "lf", longest + "\n");
        var tooLong = await SetAsync(host, "too-long", longest + "x\r\n");

        Assert.Equal(0, crlf.ExitCode);
        Assert.Equal(0, lf.ExitCode);
        Assert.Equal(1, tooLong.ExitCode);
        Assert.Equal(longest, await File.ReadAllTextAsync(FileOf(host, "crlf"), Token));
    }

    [Fact]
    public async Task A_value_that_held_bytes_other_than_utf_8_is_refused_and_not_stored()
    {
        await using var host = await RunningHost.StartAsync();

        var run = await SetAsync(host, "token", "secret-\uFFFD-value");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("Secret value is not text", run.ErrorLines[0]);
        Assert.False(File.Exists(FileOf(host, "token")));
    }

    [Fact]
    public async Task Set_without_a_name_is_not_understood()
    {
        await using var host = await RunningHost.StartAsync();

        var run = await SetAsync(host, "token", Value);
        var missing = await CliRun.ExecuteWithInputAsync(Value, host.Settings.ControlSocketPath, Language.English, "secret", "set");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(2, missing.ExitCode);
        Assert.Equal("Command not understood", missing.ErrorLines[0]);
    }

    [Theory]
    [InlineData("", "Empty secret value")]
    [InlineData("\n", "Empty secret value")]
    [InlineData("         \n", "Empty secret value")]
    [InlineData("short\n", "Secret value too short")]
    [InlineData("1234567", "Secret value too short")]
    public async Task An_empty_blank_or_short_value_is_refused_and_nothing_is_stored(string input, string title)
    {
        await using var host = await RunningHost.StartAsync();

        var run = await SetAsync(host, "token", input);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Equal(title, run.ErrorLines[0]);
        Assert.False(File.Exists(FileOf(host, "token")));
        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListSecretNamesAsync(Token));
    }

    [Fact]
    public async Task The_shortest_accepted_value_has_eight_characters_and_the_refusal_names_the_minimum()
    {
        await using var host = await RunningHost.StartAsync();

        var accepted = await SetAsync(host, "token", "12345678");
        var refused = await SetAsync(host, "other", "1234567");

        Assert.Equal(0, accepted.ExitCode);
        Assert.Equal("Nothing was stored. A secret value has at least 8 characters.", refused.ErrorLines[1]);
    }

    [Fact]
    public async Task A_value_over_the_maximum_is_refused()
    {
        await using var host = await RunningHost.StartAsync();

        var run = await SetAsync(host, "token", new string('x', 65_537));
        var longest = await SetAsync(host, "longest", new string('x', 65_536));

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("Secret value too long", run.ErrorLines[0]);
        Assert.Equal(0, longest.ExitCode);
        Assert.False(File.Exists(FileOf(host, "token")));
    }

    [Theory]
    [InlineData("Bad_Name")]
    [InlineData("a.b")]
    public async Task A_name_outside_the_rule_is_refused_without_repeating_it(string name)
    {
        await using var host = await RunningHost.StartAsync();

        var run = await SetAsync(host, name, Value);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("Invalid secret name", run.ErrorLines[0]);
        Assert.DoesNotContain(name, run.ErrorLines[1], StringComparison.Ordinal);
        Assert.False(Directory.Exists(host.Settings.SecretsDirectory));
    }

    [Theory]
    [InlineData("a/../../b")]
    [InlineData("..")]
    [InlineData(".")]
    public async Task A_name_that_is_a_path_cannot_reach_another_endpoint_or_file(string name)
    {
        await using var host = await RunningHost.StartAsync();

        var run = await SetAsync(host, name, Value);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.False(Directory.Exists(host.Settings.SecretsDirectory));
    }

    [Fact]
    public async Task List_shows_the_names_only_as_a_table_with_a_header()
    {
        await using var host = await RunningHost.StartAsync();
        await SetAsync(host, "b-secret", Value);
        await SetAsync(host, "a-secret", Value);

        var run = await RunAsync(host, Language.English, "secret", "list");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal($"Name{Environment.NewLine}a-secret{Environment.NewLine}b-secret{Environment.NewLine}", run.Output);
        Assert.DoesNotContain(Value, run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_with_json_is_the_api_document_and_names_nothing_but_names()
    {
        await using var host = await RunningHost.StartAsync();
        await SetAsync(host, "b-secret", Value);
        await SetAsync(host, "a-secret", Value);

        var english = await RunAsync(host, Language.English, "secret", "list", "--json");
        var polish = await RunAsync(host, Language.Polish, "secret", "list", "--json");

        Assert.Equal($$"""{"secrets":[{"name":"a-secret"},{"name":"b-secret"}]}{{Environment.NewLine}}""", english.Output);
        Assert.Equal(english, polish);
        using var document = JsonDocument.Parse(english.Output);
        Assert.All(document.RootElement.GetProperty("secrets").EnumerateArray(), secret => Assert.Equal(["name"], secret.EnumerateObject().Select(p => p.Name)));
    }

    [Fact]
    public async Task List_says_so_when_nothing_is_stored_and_prints_an_empty_list_under_json()
    {
        await using var host = await RunningHost.StartAsync();

        var run = await RunAsync(host, Language.English, "secret", "list");
        var json = await RunAsync(host, Language.English, "secret", "list", "--json");

        Assert.Equal($"No secrets are stored.{Environment.NewLine}", run.Output);
        Assert.Equal($$"""{"secrets":[]}{{Environment.NewLine}}""", json.Output);
    }

    [Fact]
    public async Task Remove_deletes_the_file_and_the_name_and_says_what_it_removed()
    {
        await using var host = await RunningHost.StartAsync();
        await SetAsync(host, "token", Value);

        var run = await RunAsync(host, Language.English, "secret", "remove", "token");
        var list = await RunAsync(host, Language.English, "secret", "list", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal($"Removed secret token.{Environment.NewLine}", run.Output);
        Assert.False(File.Exists(FileOf(host, "token")));
        Assert.Equal($$"""{"secrets":[]}{{Environment.NewLine}}""", list.Output);
    }

    [Fact]
    public async Task Remove_with_json_prints_the_name()
    {
        await using var host = await RunningHost.StartAsync();
        await SetAsync(host, "token", Value);

        var run = await RunAsync(host, Language.English, "secret", "remove", "token", "--json");

        Assert.Equal($$"""{"name":"token"}{{Environment.NewLine}}""", run.Output);
    }

    [Fact]
    public async Task Removing_a_secret_that_is_not_stored_is_a_failure_that_names_it()
    {
        await using var host = await RunningHost.StartAsync();

        var run = await RunAsync(host, Language.English, "secret", "remove", "never-stored");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Equal(["Secret not found", "No secret is stored under the name never-stored."], run.ErrorLines);
    }

    [Fact]
    public async Task Setting_a_name_again_replaces_its_value()
    {
        await using var host = await RunningHost.StartAsync();
        await SetAsync(host, "token", Value);

        var run = await SetAsync(host, "token", Value + "-new");
        var list = await RunAsync(host, Language.English, "secret", "list", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(Value + "-new", await File.ReadAllTextAsync(FileOf(host, "token"), Token));
        Assert.Equal($$"""{"secrets":[{"name":"token"}]}{{Environment.NewLine}}""", list.Output);
    }

    [Fact]
    public async Task The_secrets_are_still_available_when_the_host_starts_again_on_the_same_volumes()
    {
        using var installation = new TempSocket();
        await using (var first = await RunningHost.StartAsync(installation: installation))
        {
            await SetAsync(first, "token", Value);
        }

        await using var second = await RunningHost.StartAsync(installation: installation);
        var list = await RunAsync(second, Language.English, "secret", "list", "--json");

        Assert.Equal($$"""{"secrets":[{"name":"token"}]}{{Environment.NewLine}}""", list.Output);
        Assert.Equal(Value, await second.App.Services.GetRequiredService<ISecretStore>().ReadAsync("token", Token));
    }

    [Fact]
    public async Task The_sentences_are_polish_and_the_command_the_options_the_json_and_the_exit_code_are_not()
    {
        await using var host = await RunningHost.StartAsync();

        var english = await SetAsync(host, "token", Value);
        var polish = await SetAsync(host, "token", Value, Language.Polish);
        var englishJson = await SetAsync(host, "token", Value, Language.English, "--json");
        var polishJson = await SetAsync(host, "token", Value, Language.Polish, "--json");
        var englishFailure = await SetAsync(host, "token", "short", Language.English);
        var polishFailure = await SetAsync(host, "token", "short", Language.Polish);

        Assert.Equal($"Zapisano sekret token.{Environment.NewLine}", polish.Output);
        Assert.NotEqual(english.Output, polish.Output);
        Assert.Equal(englishJson, polishJson);
        Assert.Equal(englishFailure.ExitCode, polishFailure.ExitCode);
        Assert.NotEqual(englishFailure.Error, polishFailure.Error);
    }

    [Fact]
    public async Task Secret_without_a_verb_and_the_help_of_each_verb_name_the_arguments_and_the_options()
    {
        await using var host = await RunningHost.StartAsync();

        var group = await RunAsync(host, Language.English, "secret");
        var set = await RunAsync(host, Language.English, "secret", "set", "--help");
        var remove = await RunAsync(host, Language.English, "secret", "remove", "-h");
        var list = await RunAsync(host, Language.English, "secret", "list", "--help");

        Assert.StartsWith("Usage: hephaestus secret <command> [options]", group.Output, StringComparison.Ordinal);
        Assert.Contains("  set     Stores a secret whose value is read from standard input.", group.Output, StringComparison.Ordinal);
        Assert.StartsWith("Usage: hephaestus secret set <name> [options]", set.Output, StringComparison.Ordinal);
        Assert.StartsWith("Usage: hephaestus secret remove <name> [options]", remove.Output, StringComparison.Ordinal);
        Assert.StartsWith("Usage: hephaestus secret list [options]", list.Output, StringComparison.Ordinal);
        Assert.All(new[] { group, set, remove, list }, run => Assert.Equal(0, run.ExitCode));
    }

    [Fact]
    public async Task Help_of_the_whole_tool_lists_the_secret_command()
    {
        await using var host = await RunningHost.StartAsync();

        var run = await RunAsync(host, Language.English, "--help");

        Assert.Contains("  secret   Stores credentials as files outside the database.", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_that_does_not_answer_is_exit_code_1_for_every_secret_command()
    {
        using var socket = new TempSocket();

        var set = await CliRun.ExecuteWithInputAsync(Value, socket.Path, Language.English, "secret", "set", "token");
        var list = await CliRun.ExecuteAsync(socket.Path, Language.English, "secret", "list");
        var remove = await CliRun.ExecuteAsync(socket.Path, Language.English, "secret", "remove", "token");

        Assert.All(new[] { set, list, remove }, run =>
        {
            Assert.Equal(1, run.ExitCode);
            Assert.Equal("Cannot reach Hephaestus", run.ErrorLines[0]);
            Assert.DoesNotContain(Value, run.Error, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("[1,2")]
    [InlineData("{}")]
    [InlineData("{\"secrets\":null}")]
    [InlineData("{\"secrets\":[null]}")]
    public async Task A_list_the_client_cannot_read_is_a_failure_not_a_crash(string body)
    {
        await using var api = await StubApi.StartAsync(app => app.MapGet("/api/secrets", () => Results.Text(body, "application/json")));

        var list = await CliRun.ExecuteAsync(api.SocketPath, Language.English, "secret", "list");

        Assert.Equal(1, list.ExitCode);
        Assert.Equal("Unexpected answer", list.ErrorLines[0]);
        Assert.Equal(string.Empty, list.Output);
    }

    [Fact]
    public async Task A_set_the_host_fails_without_a_description_is_a_failure_not_a_crash()
    {
        await using var api = await StubApi.StartAsync(app => app.MapPut("/api/secrets/{name}", () => Results.StatusCode(500)));

        var set = await CliRun.ExecuteWithInputAsync(Value, api.SocketPath, Language.English, "secret", "set", "token");

        Assert.Equal(1, set.ExitCode);
        Assert.Equal("Unexpected answer", set.ErrorLines[0]);
        Assert.Equal(string.Empty, set.Output);
    }
}
