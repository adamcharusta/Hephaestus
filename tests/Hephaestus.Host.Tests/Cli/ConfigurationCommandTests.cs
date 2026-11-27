using Hephaestus.Core.Localization;

namespace Hephaestus.Host.Tests.Cli;

/// <summary>Story 1.9 on the command line: <c>alias</c>, <c>limit</c>, <c>protected-path</c>, <c>setting</c>, and <c>config show</c>.</summary>
public class ConfigurationCommandTests
{
    private static string Nl => Environment.NewLine;

    private static Task<CliRun> RunAsync(RunningHost host, params string[] args) =>
        CliRun.ExecuteAsync(host.Settings.ControlSocketPath, Language.English, args);

    private static Task<CliRun> RunAsync(RunningHost host, Language language, params string[] args) =>
        CliRun.ExecuteAsync(host.Settings.ControlSocketPath, language, args);

    [Fact]
    public async Task Adding_an_Alias_says_so_lists_it_and_removing_it_says_what_was_removed()
    {
        await using var host = await RunningHost.StartAsync();

        var add = await RunAsync(host, "alias", "add", "opus5.5", "--agent", "claude-code", "--model", "claude-opus-5-5");
        var effort = await RunAsync(host, "alias", "add", "deep", "--effort", "high");
        var list = await RunAsync(host, "alias", "list");
        var remove = await RunAsync(host, "alias", "remove", "deep");

        Assert.Equal(0, add.ExitCode);
        Assert.Equal($"Added the Alias opus5.5: Agent claude-code, Model claude-opus-5-5.{Nl}", add.Output);
        Assert.Equal($"Added the Alias deep: Effort high.{Nl}", effort.Output);
        var lines = list.Output.Split(Nl, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Matches(@"^Alias +Agent +Model +Effort +Added$", lines[0]);
        Assert.Matches(@"^deep +none +none +high +\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", lines[1]);
        Assert.Matches(@"^opus5\.5 +claude-code +claude-opus-5-5 +none +\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", lines[2]);
        Assert.Equal($"Removed the Alias deep (Effort high).{Nl}", remove.Output);
        Assert.Equal(string.Empty, add.Error + effort.Error + list.Error + remove.Error);
    }

    [Fact]
    public async Task Defining_an_Alias_again_says_it_is_already_defined_and_exits_0()
    {
        await using var host = await RunningHost.StartAsync();
        await RunAsync(host, "alias", "add", "opus", "--agent", "codex-cli", "--model", "gpt-5.5");
        await RunAsync(host, "alias", "add", "deep", "--effort", "low");

        var model = await RunAsync(host, "alias", "add", "opus", "--agent", "codex-cli", "--model", "gpt-5.5");
        var effort = await RunAsync(host, "alias", "add", "deep", "--effort", "low");

        Assert.Equal(0, model.ExitCode);
        Assert.Equal($"The Alias opus (Agent codex-cli, Model gpt-5.5) is already defined. Nothing changed.{Nl}", model.Output);
        Assert.Equal($"The Alias deep (Effort low) is already defined. Nothing changed.{Nl}", effort.Output);
    }

    [Fact]
    public async Task With_no_Alias_the_list_says_so_and_json_is_the_API_document()
    {
        await using var host = await RunningHost.StartAsync();

        var list = await RunAsync(host, "alias", "list");
        var json = await RunAsync(host, "alias", "list", "--json");

        Assert.Equal($"No Aliases.{Nl}", list.Output);
        Assert.Equal($$"""{"aliases":[]}{{Nl}}""", json.Output);
    }

    [Theory]
    [InlineData("Unknown Agent", "Nothing changed. 'gemini' is not an Agent Hephaestus drives. Use one of: claude-code, codex-cli.", "alias", "add", "x", "--agent", "gemini", "--model", "m")]
    [InlineData("Unknown Effort", "Nothing changed. 'ultra' is not an Effort. Use one of: low, medium, high, xhigh, max.", "alias", "add", "x", "--effort", "ultra")]
    [InlineData("An Alias names an Agent and a Model, or an Effort", "Nothing changed. Give --agent and --model together, or --effort alone.", "alias", "add", "x", "--agent", "claude-code")]
    [InlineData("An Alias names an Agent and a Model, or an Effort", "Nothing changed. Give --agent and --model together, or --effort alone.", "alias", "add", "x")]
    [InlineData("Alias not found", "nope is not an Alias. Nothing changed. Run alias list to see the Aliases.", "alias", "remove", "nope")]
    public async Task A_refusal_is_the_title_then_the_detail_on_standard_error_with_exit_code_1(string title, string detail, params string[] args)
    {
        await using var host = await RunningHost.StartAsync();

        var run = await RunAsync(host, args);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Equal([title, detail], run.ErrorLines);
    }

    [Fact]
    public async Task Redefining_an_Alias_says_to_remove_it_first_and_the_stored_one_stays()
    {
        await using var host = await RunningHost.StartAsync();
        await RunAsync(host, "alias", "add", "opus", "--agent", "claude-code", "--model", "claude-opus-5-5");

        var run = await RunAsync(host, "alias", "add", "opus", "--agent", "codex-cli", "--model", "gpt-5.5");
        var list = await RunAsync(host, "alias", "list", "--json");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("The Alias is already defined", run.ErrorLines[0]);
        Assert.Contains("alias remove opus", run.ErrorLines[1], StringComparison.Ordinal);
        Assert.Contains("claude-opus-5-5", list.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Limits_are_shown_as_none_until_set_then_set_and_shown_and_a_change_keeps_the_rest()
    {
        await using var host = await RunningHost.StartAsync();

        var before = await RunAsync(host, "limit", "show");
        var set = await RunAsync(host, "limit", "set", "--task-duration", "2h", "--cpus", "2", "--memory", "4g");
        var memory = await RunAsync(host, "limit", "set", "--memory", "8g");
        var show = await RunAsync(host, "limit", "show", "--json");

        Assert.Equal($"Task duration: none{Nl}Processors: none{Nl}Memory: none{Nl}", before.Output);
        Assert.Equal($"Task duration: 2h{Nl}Processors: 2{Nl}Memory: 4g{Nl}", set.Output);
        Assert.Equal($"Task duration: 2h{Nl}Processors: 2{Nl}Memory: 8g{Nl}", memory.Output);
        Assert.Equal($$"""{"taskDuration":"2h","cpus":"2","memory":"8g"}{{Nl}}""", show.Output);
    }

    [Theory]
    [InlineData("Invalid Task duration limit", "limit", "set", "--task-duration", "soon")]
    [InlineData("Invalid processor limit", "limit", "set", "--cpus", "lots")]
    [InlineData("Invalid memory limit", "limit", "set", "--memory", "plenty")]
    [InlineData("No limit given", "limit", "set")]
    public async Task A_limit_out_of_shape_or_none_given_is_a_failure_that_changes_nothing(string title, params string[] args)
    {
        await using var host = await RunningHost.StartAsync();

        var run = await RunAsync(host, args);
        var show = await RunAsync(host, "limit", "show", "--json");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(title, run.ErrorLines[0]);
        Assert.Equal($$"""{"taskDuration":null,"cpus":null,"memory":null}{{Nl}}""", show.Output);
    }

    [Fact]
    public async Task A_new_installation_protects_github_and_hephaestus_yml_and_the_Owner_changes_the_list()
    {
        await using var host = await RunningHost.StartAsync();

        var list = await RunAsync(host, "protected-path", "list");
        var add = await RunAsync(host, "protected-path", "add", "docs/**");
        var again = await RunAsync(host, "protected-path", "add", "docs/**");
        var remove = await RunAsync(host, "protected-path", "remove", ".github/**");
        var after = await RunAsync(host, "protected-path", "list", "--json");

        Assert.Equal($"Protected path{Nl}.github/**{Nl}hephaestus.yml{Nl}", list.Output);
        Assert.Equal($"Added the protected path docs/**.{Nl}", add.Output);
        Assert.Equal($"docs/** is already a protected path. Nothing added.{Nl}", again.Output);
        Assert.Equal($"Removed the protected path .github/**.{Nl}", remove.Output);
        Assert.Equal($$"""{"protectedPaths":["docs/**","hephaestus.yml"]}{{Nl}}""", after.Output);
    }

    [Fact]
    public async Task With_every_protected_path_removed_the_list_says_none()
    {
        await using var host = await RunningHost.StartAsync();
        await RunAsync(host, "protected-path", "remove", ".github/**");
        await RunAsync(host, "protected-path", "remove", "hephaestus.yml");

        var list = await RunAsync(host, "protected-path", "list");

        Assert.Equal($"No protected paths.{Nl}", list.Output);
    }

    [Theory]
    [InlineData("Invalid protected path", "protected-path", "add", "../outside")]
    [InlineData("Protected path not found", "protected-path", "remove", "docs/**")]
    public async Task A_glob_that_is_unsafe_or_not_listed_is_a_failure(string title, params string[] args)
    {
        await using var host = await RunningHost.StartAsync();

        var run = await RunAsync(host, args);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(title, run.ErrorLines[0]);
    }

    [Fact]
    public async Task A_glob_with_characters_an_address_treats_specially_reaches_the_host_as_typed()
    {
        await using var host = await RunningHost.StartAsync();

        await RunAsync(host, "protected-path", "add", "a&b#c?d=e/**");
        var list = await RunAsync(host, "protected-path", "list", "--json");
        var remove = await RunAsync(host, "protected-path", "remove", "a&b#c?d=e/**");

        Assert.Contains("a\\u0026b#c?d=e/**", list.Output, StringComparison.Ordinal);
        Assert.Equal($"Removed the protected path a&b#c?d=e/**.{Nl}", remove.Output);
    }

    [Fact]
    public async Task Settings_list_the_language_and_the_polling_interval_with_their_defaults_and_a_new_installation_is_in_English()
    {
        await using var host = await RunningHost.StartAsync();

        var list = await RunAsync(host, "setting", "list");

        Assert.Equal($"Setting          Value  Default{Nl}language         en     en{Nl}pollingInterval  60     60{Nl}", list.Output);
    }

    [Fact]
    public async Task The_language_the_Owner_sets_is_used_from_the_next_command_and_stays_until_changed()
    {
        await using var host = await RunningHost.StartAsync();
        var socket = host.Settings.ControlSocketPath;

        var before = await CliRun.ExecuteInStoredLanguageAsync(socket, "version");
        var set = await CliRun.ExecuteInStoredLanguageAsync(socket, "setting", "set", "language", "pl");
        var after = await CliRun.ExecuteInStoredLanguageAsync(socket, "version");
        var failure = await CliRun.ExecuteInStoredLanguageAsync(socket, "setting", "set", "language", "tlh");
        var help = await CliRun.ExecuteInStoredLanguageAsync(socket, "limit", "show", "--help");
        var back = await CliRun.ExecuteInStoredLanguageAsync(socket, "setting", "set", "language", "en");
        var english = await CliRun.ExecuteInStoredLanguageAsync(socket, "version");

        Assert.StartsWith("Hephaestus version ", before.Output, StringComparison.Ordinal);
        Assert.Equal($"Set language to pl.{Nl}", set.Output);
        Assert.StartsWith("Hephaestus w wersji ", after.Output, StringComparison.Ordinal);
        Assert.Equal(1, failure.ExitCode);
        Assert.Equal("Nieprawidłowa wartość", failure.ErrorLines[0]);
        Assert.Contains("Pokazuje limity instalacji.", help.Output, StringComparison.Ordinal);
        Assert.Equal($"Ustawiono language na en.{Nl}", back.Output);
        Assert.StartsWith("Hephaestus version ", english.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_language_survives_a_restart_of_the_host()
    {
        using var installation = new TempSocket();
        await using (var first = await RunningHost.StartAsync(installation: installation))
        {
            await CliRun.ExecuteInStoredLanguageAsync(first.Settings.ControlSocketPath, "setting", "set", "language", "pl");
        }

        await using var second = await RunningHost.StartAsync(installation: installation);
        var run = await CliRun.ExecuteInStoredLanguageAsync(second.Settings.ControlSocketPath, "version");

        Assert.StartsWith("Hephaestus w wersji ", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_environments_language_serves_an_installation_that_stored_none_and_the_stored_one_wins_when_there_is_one()
    {
        await using var host = await RunningHost.StartAsync();
        var socket = host.Settings.ControlSocketPath;

        var fallback = await RunAsync(host, Language.Polish, "version");
        await CliRun.ExecuteInStoredLanguageAsync(socket, "setting", "set", "language", "en");
        var stored = await RunAsync(host, Language.Polish, "version");
        var listed = await RunAsync(host, Language.Polish, "setting", "list", "--json");

        Assert.StartsWith("Hephaestus w wersji ", fallback.Output, StringComparison.Ordinal);
        Assert.StartsWith("Hephaestus version ", stored.Output, StringComparison.Ordinal);
        Assert.Contains("\"isSet\":true", listed.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_command_not_only_the_new_ones_writes_in_the_stored_language()
    {
        await using var host = await RunningHost.StartAsync();
        var socket = host.Settings.ControlSocketPath;
        await CliRun.ExecuteInStoredLanguageAsync(socket, "setting", "set", "language", "pl");

        var secrets = await CliRun.ExecuteInStoredLanguageAsync(socket, "secret", "list");
        var users = await CliRun.ExecuteInStoredLanguageAsync(socket, "trusted-user", "list");
        var removal = await CliRun.ExecuteInStoredLanguageAsync(socket, "trusted-user", "remove", "nobody");
        var github = await CliRun.ExecuteInStoredLanguageAsync(socket, "github", "check");

        Assert.Equal($"Nie zapisano żadnych sekretów.{Nl}", secrets.Output);
        Assert.Equal($"Brak zaufanych użytkowników.{Nl}", users.Output);
        Assert.Equal("Nie znaleziono zaufanego użytkownika", removal.ErrorLines[0]);
        Assert.Equal(1, github.ExitCode);
        Assert.NotEqual("No GitHub App is connected", github.ErrorLines[0]);
    }

    [Fact]
    public async Task A_failure_the_command_line_reports_itself_is_in_the_stored_language_too()
    {
        await using var api = await StubApi.StartAsync(app =>
        {
            app.MapGet("/api/settings", () => Results.Text("""{"settings":[{"name":"language","value":"pl","default":"en","isSet":true}]}""", "application/json"));
            app.MapGet("/api/version", () => Results.Text("not json", "application/json"));
        });

        var run = await CliRun.ExecuteInStoredLanguageAsync(api.SocketPath, "version");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("Nieoczekiwana odpowiedź", run.ErrorLines[0]);
    }

    [Fact]
    public async Task A_host_that_accepts_the_connection_and_never_answers_delays_the_language_lookup_only_by_its_timeout()
    {
        await using var api = await StubApi.StartAsync(app =>
        {
            app.MapGet("/api/settings", async (HttpContext context) =>
            {
                await Task.Delay(Timeout.Infinite, context.RequestAborted);
                return Results.Empty;
            });
            app.MapGet("/api/version", () => Results.Text("""{"version":"1.2.3"}""", "application/json"));
        });
        var started = DateTimeOffset.UtcNow;

        var run = await CliRun.ExecuteInStoredLanguageAsync(api.SocketPath, "version");

        Assert.Equal($"Hephaestus version 1.2.3{Nl}", run.Output);
        Assert.InRange(DateTimeOffset.UtcNow - started, Hephaestus.Host.Cli.CliApplication.LanguageLookupTimeout, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Without_a_stored_language_or_an_answering_host_the_command_line_writes_English()
    {
        using var socket = new TempSocket();

        var run = await CliRun.ExecuteInStoredLanguageAsync(socket.Path, "version");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("Cannot reach Hephaestus", run.ErrorLines[0]);
    }

    [Theory]
    [InlineData("Unknown setting", "setting", "set", "colour", "blue")]
    [InlineData("Invalid value", "setting", "set", "language", "klingon")]
    public async Task A_setting_that_does_not_exist_or_a_value_it_does_not_accept_is_a_failure(string title, params string[] args)
    {
        await using var host = await RunningHost.StartAsync();

        var run = await RunAsync(host, args);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(title, run.ErrorLines[0]);
    }

    [Fact]
    public async Task The_configuration_shows_every_section_and_no_secret_value()
    {
        await using var host = await RunningHost.StartAsync();
        const string secretValue = "sk-test-0123456789-abcdefghij";
        await CliRun.ExecuteWithInputAsync(secretValue + "\n", host.Settings.ControlSocketPath, Language.English, "secret", "set", "anthropic");
        await RunAsync(host, "alias", "add", "opus", "--agent", "claude-code", "--model", "claude-opus-5-5");
        await RunAsync(host, "limit", "set", "--task-duration", "90m");

        var show = await RunAsync(host, "config", "show");
        var json = await RunAsync(host, "config", "show", "--json");

        Assert.Equal(0, show.ExitCode);
        var lines = show.Output.Split(Nl);
        Assert.Equal(["Trusted Users", "No Trusted Users.", string.Empty, "Aliases"], lines[..4]);
        foreach (var section in new[] { "Limits", "Protected paths", "Settings", "Secrets (names only)" })
        {
            Assert.Contains(section, lines);
        }

        Assert.Contains("Task duration: 1h30m", lines);
        Assert.Contains("Processors: none", lines);
        Assert.Contains(".github/**", lines);
        Assert.Contains("anthropic", lines);
        Assert.Matches(@"^opus +claude-code +claude-opus-5-5 +none +\d", lines.Single(l => l.StartsWith("opus", StringComparison.Ordinal)));
        Assert.DoesNotContain(secretValue, show.Output + json.Output, StringComparison.Ordinal);
        Assert.Contains("\"secrets\":[{\"name\":\"anthropic\"}]", json.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_nothing_stored_the_configuration_says_so_in_each_section()
    {
        await using var host = await RunningHost.StartAsync();

        var show = await RunAsync(host, "config", "show");

        var lines = show.Output.Split(Nl);
        Assert.Contains("No Aliases.", lines);
        Assert.Contains("No Trusted Users.", lines);
        Assert.Contains("No secrets are stored.", lines);
    }

    [Fact]
    public async Task In_Polish_the_sentences_and_headers_differ_and_the_names_and_json_do_not()
    {
        await using var host = await RunningHost.StartAsync();

        var add = await RunAsync(host, Language.Polish, "alias", "add", "opus", "--agent", "claude-code", "--model", "claude-opus-5-5");
        var effort = await RunAsync(host, Language.Polish, "alias", "add", "deep", "--effort", "high");
        var list = await RunAsync(host, Language.Polish, "alias", "list");
        var limits = await RunAsync(host, Language.Polish, "limit", "set", "--cpus", "1.5");
        var refused = await RunAsync(host, Language.Polish, "alias", "add", "x", "--agent", "gemini", "--model", "m");
        var json = await RunAsync(host, Language.Polish, "alias", "list", "--json");
        var english = await RunAsync(host, Language.English, "alias", "list", "--json");

        Assert.Equal($"Dodano alias opus: agent claude-code, model claude-opus-5-5.{Nl}", add.Output);
        Assert.Equal($"Dodano alias deep: poziom wysiłku high.{Nl}", effort.Output);
        Assert.StartsWith("Alias  Agent        Model            Poziom wysiłku  Dodano", list.Output, StringComparison.Ordinal);
        Assert.Equal($"Czas zadania: none{Nl}Procesory: 1.5{Nl}Pamięć: none{Nl}", limits.Output);
        Assert.Equal("Nieznany agent", refused.ErrorLines[0]);
        Assert.Equal(english.Output, json.Output);
    }

    [Fact]
    public async Task Each_new_group_and_command_has_its_own_help_with_its_options()
    {
        using var socket = new TempSocket();

        var root = await CliRun.ExecuteAsync(socket.Path, Language.English, "--help");
        var alias = await CliRun.ExecuteAsync(socket.Path, Language.English, "alias", "--help");
        var aliasAdd = await CliRun.ExecuteAsync(socket.Path, Language.English, "alias", "add", "--help");
        var limitSet = await CliRun.ExecuteAsync(socket.Path, Language.English, "limit", "set", "--help");
        var setting = await CliRun.ExecuteAsync(socket.Path, Language.English, "setting");
        var config = await CliRun.ExecuteAsync(socket.Path, Language.English, "config");

        foreach (var command in new[] { "alias", "limit", "protected-path", "setting", "config" })
        {
            Assert.Contains($"  {command.PadRight(14)}  ", root.Output, StringComparison.Ordinal);
        }

        Assert.StartsWith("Usage: hephaestus alias <command> [options]", alias.Output, StringComparison.Ordinal);
        Assert.StartsWith("Usage: hephaestus alias add <name> [options]", aliasAdd.Output, StringComparison.Ordinal);
        foreach (var option in new[] { "--agent", "--model", "--effort", "--json" })
        {
            Assert.Contains(option, aliasAdd.Output, StringComparison.Ordinal);
        }

        foreach (var option in new[] { "--task-duration", "--cpus", "--memory" })
        {
            Assert.Contains(option, limitSet.Output, StringComparison.Ordinal);
        }

        Assert.StartsWith("Usage: hephaestus setting <command> [options]", setting.Output, StringComparison.Ordinal);
        Assert.StartsWith("Usage: hephaestus config <command> [options]", config.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_help_of_an_option_names_every_Agent_and_every_Effort_Core_knows()
    {
        using var socket = new TempSocket();

        var help = await CliRun.ExecuteAsync(socket.Path, Language.English, "alias", "add", "--help");

        foreach (var tag in Core.Aliases.Agents.Tags.Concat(Core.Aliases.Efforts.Tags))
        {
            Assert.Contains(tag, help.Output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("alias", "add")]
    [InlineData("alias", "remove")]
    [InlineData("alias", "list", "extra")]
    [InlineData("alias", "add", "x", "--agent")]
    [InlineData("limit", "set", "--cpus")]
    [InlineData("limit", "show", "extra")]
    [InlineData("protected-path", "add")]
    [InlineData("setting", "set", "language")]
    [InlineData("config", "show", "extra")]
    [InlineData("config", "dump")]
    public async Task A_command_that_is_not_understood_is_exit_code_2_with_nothing_printed(params string[] args)
    {
        using var socket = new TempSocket();

        var run = await CliRun.ExecuteAsync(socket.Path, Language.English, args);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
    }

    [Theory]
    [InlineData("alias", "list")]
    [InlineData("alias", "add", "x", "--effort", "low")]
    [InlineData("alias", "remove", "x")]
    [InlineData("limit", "show")]
    [InlineData("limit", "set", "--cpus", "1")]
    [InlineData("protected-path", "list")]
    [InlineData("protected-path", "add", "x")]
    [InlineData("protected-path", "remove", "x")]
    [InlineData("setting", "list")]
    [InlineData("setting", "set", "language", "pl")]
    [InlineData("config", "show")]
    public async Task With_no_host_listening_each_command_says_so_and_exits_1(params string[] args)
    {
        using var socket = new TempSocket();

        var run = await CliRun.ExecuteAsync(socket.Path, Language.English, args);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Equal("Cannot reach Hephaestus", run.ErrorLines[0]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("""{"aliases":null,"protectedPaths":null,"settings":null}""")]
    [InlineData("""{"aliases":[null],"protectedPaths":[null],"settings":[null]}""")]
    [InlineData("""{"name":null,"pattern":null,"value":null}""")]
    public async Task An_answer_the_command_cannot_read_is_a_failure_and_prints_nothing(string body)
    {
        await using var api = await StubApi.StartAsync(app =>
        {
            app.MapGet("/api/settings", () => Results.Text("{}", "application/json"));
            app.MapGet("/api/{**rest}", () => Results.Text(body, "application/json"));
            app.MapPut("/api/{**rest}", () => Results.Text(body, "application/json", statusCode: 201));
            app.MapDelete("/api/{**rest}", () => Results.Text(body, "application/json"));
        });
        string[][] commands =
        [
            ["alias", "list"],
            ["alias", "add", "x", "--effort", "low"],
            ["alias", "remove", "x"],
            ["protected-path", "list"],
            ["protected-path", "add", "x"],
            ["protected-path", "remove", "x"],
            ["setting", "list"],
            ["setting", "set", "language", "pl"],
            ["config", "show"],
        ];

        foreach (var args in commands)
        {
            var run = await CliRun.ExecuteInStoredLanguageAsync(api.SocketPath, args);

            Assert.True(run.ExitCode == 1, $"'{string.Join(' ', args)}' answered {body} with exit code {run.ExitCode}");
            Assert.Equal(string.Empty, run.Output);
            Assert.Equal("Unexpected answer", run.ErrorLines[0]);
        }
    }

    [Theory]
    [InlineData("""{"trustedUsers":[],"aliases":[],"limits":null,"protectedPaths":[],"settings":[],"secrets":[]}""")]
    [InlineData("""{"trustedUsers":[null],"aliases":[],"limits":{},"protectedPaths":[],"settings":[],"secrets":[]}""")]
    [InlineData("""{"trustedUsers":[],"aliases":[{"name":"x"}],"limits":{},"protectedPaths":[],"settings":[],"secrets":[]}""")]
    [InlineData("""{"trustedUsers":[],"aliases":[],"limits":{},"protectedPaths":[null],"settings":[],"secrets":[]}""")]
    [InlineData("""{"trustedUsers":[],"aliases":[],"limits":{},"protectedPaths":[],"settings":[{"name":"language"}],"secrets":[]}""")]
    [InlineData("""{"trustedUsers":[],"aliases":[],"limits":{},"protectedPaths":[],"settings":[],"secrets":[{}]}""")]
    [InlineData("""{"trustedUsers":[],"aliases":[],"limits":{},"protectedPaths":[]}""")]
    public async Task A_configuration_with_a_member_missing_or_null_where_a_value_must_be_is_a_failure_and_prints_nothing(string body)
    {
        await using var api = await StubApi.StartAsync(app =>
        {
            app.MapGet("/api/settings", () => Results.Text("{}", "application/json"));
            app.MapGet("/api/configuration", () => Results.Text(body, "application/json"));
        });

        var run = await CliRun.ExecuteInStoredLanguageAsync(api.SocketPath, "config", "show");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Equal("Unexpected answer", run.ErrorLines[0]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    public async Task A_limits_answer_that_is_no_document_is_a_failure_and_prints_nothing(string body)
    {
        await using var api = await StubApi.StartAsync(app =>
        {
            app.MapGet("/api/settings", () => Results.Text("{}", "application/json"));
            app.MapGet("/api/limits", () => Results.Text(body, "application/json"));
            app.MapPut("/api/limits", () => Results.Text(body, "application/json"));
        });

        foreach (var args in new[] { new[] { "limit", "show" }, ["limit", "set", "--cpus", "1"] })
        {
            var run = await CliRun.ExecuteInStoredLanguageAsync(api.SocketPath, args);

            Assert.Equal(1, run.ExitCode);
            Assert.Equal(string.Empty, run.Output);
            Assert.Equal("Unexpected answer", run.ErrorLines[0]);
        }
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"settings":[{"name":"language","value":"tlh","default":"en","isSet":true}]}""")]
    [InlineData("""{"settings":[{"name":"language","value":"pl","default":"en","isSet":false}]}""")]
    [InlineData("""{"settings":[{"name":"other","value":"pl","default":"en","isSet":true}]}""")]
    [InlineData("""{"settings":[null]}""")]
    public async Task A_host_that_does_not_name_a_stored_language_leaves_the_command_line_in_English(string settings)
    {
        await using var api = await StubApi.StartAsync(app =>
        {
            app.MapGet("/api/settings", () => Results.Text(settings, "application/json"));
            app.MapGet("/api/version", () => Results.Text("""{"version":"1.2.3"}""", "application/json"));
        });

        var run = await CliRun.ExecuteInStoredLanguageAsync(api.SocketPath, "version");

        Assert.Equal($"Hephaestus version 1.2.3{Nl}", run.Output);
    }

    [Fact]
    public async Task A_host_that_answers_the_settings_with_an_error_leaves_the_command_line_in_English()
    {
        await using var api = await StubApi.StartAsync(app =>
        {
            app.MapGet("/api/settings", () => Results.StatusCode(500));
            app.MapGet("/api/version", () => Results.Text("""{"version":"1.2.3"}""", "application/json"));
        });

        var run = await CliRun.ExecuteInStoredLanguageAsync(api.SocketPath, "version");

        Assert.Equal($"Hephaestus version 1.2.3{Nl}", run.Output);
    }

    [Fact]
    public async Task A_stored_language_the_host_names_is_the_language_of_the_command()
    {
        await using var api = await StubApi.StartAsync(app =>
        {
            app.MapGet("/api/settings", () => Results.Text("""{"settings":[{"name":"language","value":"pl","default":"en","isSet":true}]}""", "application/json"));
            app.MapGet("/api/version", () => Results.Text("""{"version":"1.2.3"}""", "application/json"));
        });

        var run = await CliRun.ExecuteInStoredLanguageAsync(api.SocketPath, "version");

        Assert.Equal($"Hephaestus w wersji 1.2.3{Nl}", run.Output);
    }
}
