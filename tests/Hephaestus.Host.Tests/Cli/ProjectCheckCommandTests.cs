using System.Text;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;

namespace Hephaestus.Host.Tests.Cli;

/// <summary>Story 1.11 on the command line: <c>project check</c> reads <c>hephaestus.yml</c> from the Base Branch and tells the Owner exactly what is wrong with it.</summary>
public class ProjectCheckCommandTests
{
    private const string Commit = "3f2a9c1d5e7b8a0c4d6e8f0a1b2c3d4e5f607182";
    private const string Valid = "image: node:22\nvalidation:\n  - npm test\n";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FakeCodeHost GatewayWith(string? content)
    {
        var gateway = new FakeCodeHost();
        gateway.Files[(101, "main")] = (Commit, content is null ? null : Encoding.UTF8.GetBytes(content));
        return gateway;
    }

    private static async Task<RunningHost> StartRegisteredAsync(FakeCodeHost gateway, Language language = Language.English)
    {
        var host = await RunningHost.StartAsync(language, configure: builder => builder.Services.AddSingleton<ICodeHost>(gateway));
        var run = await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "main");
        Assert.Equal(0, run.ExitCode);
        return host;
    }

    private static Task<CliRun> RunAsync(RunningHost host, Language language, params string[] args) =>
        CliRun.ExecuteAsync(host.Settings.ControlSocketPath, language, args);

    private static Task<CliRun> RunAsync(RunningHost host, params string[] args) => RunAsync(host, Language.English, args);

    private static string[] Lines(string text) => text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public async Task A_valid_file_is_reported_valid_names_the_commit_and_lists_the_settings_and_exits_0()
    {
        await using var host = await StartRegisteredAsync(GatewayWith(Valid + "correctionRounds: 3\nlimits:\n  memory: 4g\nallowedEgressHosts: [api.nuget.org, Registry.NPMJS.org]\nlanguage: pl\n"));

        var run = await RunAsync(host, "project", "check", "acme/shop");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        Assert.Equal(
            [
                $"hephaestus.yml of acme/shop is valid. It was read from commit {Commit} of Base Branch main.",
                "primaryAlias: none",
                "fallbackAlias: none",
                "effort: none",
                "methodology: default",
                "validation: npm test",
                "correctionRounds: 3",
                "image: node:22",
                "limits.taskDuration: none",
                "limits.cpus: none",
                "limits.memory: 4g",
                "allowedEgressHosts: api.nuget.org, registry.npmjs.org",
                "language: pl",
            ],
            Lines(run.Output));
    }

    [Fact]
    public async Task A_missing_file_is_reported_missing_with_the_commit_and_exits_1()
    {
        await using var host = await StartRegisteredAsync(GatewayWith(null));

        var run = await RunAsync(host, "project", "check", "acme/shop");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        var lines = Lines(run.Output);
        Assert.Single(lines);
        Assert.StartsWith($"acme/shop has no hephaestus.yml at the root of Base Branch main (commit {Commit}).", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_invalid_setting_is_named_with_its_line_and_its_reason_and_the_command_exits_1()
    {
        await using var host = await StartRegisteredAsync(GatewayWith("image: node:22\nmethodology: nope\nlanguage: fr\nsurprise: 1\nlimits:\n  disk: 1g\n"));

        var run = await RunAsync(host, "project", "check", "acme/shop");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        var lines = Lines(run.Output);
        Assert.StartsWith($"hephaestus.yml of acme/shop is not valid. It was read from commit {Commit} of Base Branch main. Problems found: 5.", lines[0], StringComparison.Ordinal);
        Assert.Matches(@"^Setting +Line +Problem$", lines[1]);
        Assert.Matches(@"^methodology +2 +Methodology nope is not available on this installation\. Available: default\.$", lines[2]);
        Assert.Matches(@"^language +3 +'fr' is not a language\. Write en or pl\.$", lines[3]);
        Assert.Matches(@"^surprise +4 +This is not a setting\. The settings allowed here are: primaryAlias, fallbackAlias, ", lines[4]);
        Assert.Matches(@"^limits\.disk +6 +This is not a setting\. The settings allowed here are: taskDuration, cpus, memory\.$", lines[5]);
        Assert.Matches(@"^validation +none +No Validation command is named\. With none, Validation would pass without checking anything\. ", lines[6]);
        Assert.Equal(7, lines.Length);
    }

    [Fact]
    public async Task The_sentences_are_polish_and_the_keys_and_commands_are_not()
    {
        await using var host = await StartRegisteredAsync(GatewayWith("image: node:22\nlanguage: fr\n"), Language.Polish);

        var invalid = await RunAsync(host, Language.Polish, "project", "check", "acme/shop");

        Assert.Equal(1, invalid.ExitCode);
        var lines = Lines(invalid.Output);
        Assert.StartsWith($"hephaestus.yml projektu acme/shop jest niepoprawny. Odczytano go z commita {Commit} gałęzi bazowej main. Znalezione problemy: 2.", lines[0], StringComparison.Ordinal);
        Assert.Matches(@"^Ustawienie +Wiersz +Problem$", lines[1]);
        Assert.Matches(@"^language +2 +'fr' nie jest językiem\. Zapisz en lub pl\.$", lines[2]);
        Assert.Matches(@"^validation +none +Nie podano żadnego polecenia walidacji\.", lines[3]);
    }

    [Fact]
    public async Task A_valid_file_is_reported_in_polish()
    {
        await using var host = await StartRegisteredAsync(GatewayWith(Valid));

        var run = await RunAsync(host, Language.Polish, "project", "check", "acme/shop");

        Assert.Equal(0, run.ExitCode);
        Assert.StartsWith($"hephaestus.yml projektu acme/shop jest poprawny. Odczytano go z commita {Commit} gałęzi bazowej main.", Lines(run.Output)[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_output_is_the_document_of_the_API_in_every_language_and_the_exit_code_still_says_whether_the_file_is_valid()
    {
        await using var valid = await StartRegisteredAsync(GatewayWith(Valid));
        await using var invalid = await StartRegisteredAsync(GatewayWith("methodology: nope\n"));
        await using var missing = await StartRegisteredAsync(GatewayWith(null));

        var validEnglish = await RunAsync(valid, Language.English, "project", "check", "acme/shop", "--json");
        var validPolish = await RunAsync(valid, Language.Polish, "project", "check", "acme/shop", "--json");
        var invalidRun = await RunAsync(invalid, "project", "check", "acme/shop", "--json");
        var missingRun = await RunAsync(missing, "project", "check", "acme/shop", "--json");

        Assert.Equal((0, 0), (validEnglish.ExitCode, validPolish.ExitCode));
        Assert.Equal(validEnglish.Output, validPolish.Output);
        Assert.Matches($$$"""^\{"repositoryId":101,"name":"acme/shop","baseBranch":"main","status":"valid","commitId":"{{{Commit}}}","issues":\[\],"configuration":\{.*\}\}\r?\n$""", validEnglish.Output);
        Assert.Equal(1, invalidRun.ExitCode);
        Assert.Contains("\"status\":\"invalid\"", invalidRun.Output, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"config.issue.methodology-unavailable\"", invalidRun.Output, StringComparison.Ordinal);
        Assert.Equal(1, missingRun.ExitCode);
        Assert.Contains("\"status\":\"missing\"", missingRun.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_name_that_is_not_a_registered_Project_fails_with_the_problem_and_exit_1()
    {
        await using var host = await StartRegisteredAsync(GatewayWith(Valid));

        var run = await RunAsync(host, "project", "check", "acme/api");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Equal("Project not found", run.ErrorLines[0]);
        Assert.StartsWith("acme/api is not a registered Project.", run.ErrorLines[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("shop")]
    [InlineData("../secrets/token")]
    [InlineData("a/b/c")]
    public async Task A_name_that_is_not_owner_slash_repo_is_refused_and_GitHub_is_not_read(string name)
    {
        var gateway = GatewayWith(Valid);
        await using var host = await StartRegisteredAsync(gateway);

        var run = await RunAsync(host, "project", "check", name);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("Invalid repository name", run.ErrorLines[0]);
        Assert.Empty(gateway.FileReads);
    }

    [Fact]
    public async Task A_Base_Branch_that_has_been_deleted_is_reported_as_such_and_not_as_a_missing_file()
    {
        var gateway = GatewayWith(Valid);
        await using var host = await StartRegisteredAsync(gateway);
        gateway.Files.Clear();

        var run = await RunAsync(host, "project", "check", "acme/shop");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(["The Base Branch does not exist", "acme/shop has no branch named main. Nothing was changed."], run.ErrorLines);
    }

    [Fact]
    public async Task GitHub_being_unreachable_is_reported_as_the_other_commands_report_it()
    {
        var gateway = GatewayWith(Valid);
        await using var host = await StartRegisteredAsync(gateway);
        gateway.FileReadFails = GitHubFileFailure.Unreachable;

        var run = await RunAsync(host, "project", "check", "acme/shop");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.NotEmpty(run.ErrorLines);
    }

    [Fact]
    public async Task Text_from_the_file_cannot_write_into_the_terminal()
    {
        var file = "image: node:22\nvalidation:\n  - \"echo \\u202Eevil\"\n\"\\e[31mred\": 1\n";
        await using var invalid = await StartRegisteredAsync(GatewayWith(file));
        await using var valid = await StartRegisteredAsync(GatewayWith("image: node:22\nvalidation:\n  - \"echo \\u202Eevil \\u2028done\"\n"));

        var invalidRun = await RunAsync(invalid, "project", "check", "acme/shop");
        var validRun = await RunAsync(valid, "project", "check", "acme/shop");

        Assert.DoesNotContain('\u001b', invalidRun.Output);
        Assert.Contains("?[31mred", invalidRun.Output, StringComparison.Ordinal);
        Assert.Equal(0, validRun.ExitCode);
        Assert.Contains("validation: echo ?evil ?done", validRun.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(validRun.Output, c => c is '\u202E' or '\u2028');
    }

    [Fact]
    public async Task A_multi_line_command_is_printed_on_one_line_as_what_would_run()
    {
        await using var host = await StartRegisteredAsync(GatewayWith("image: node:22\nvalidation:\n  - |\n    set -e\n    npm ci\n  - \"a\\tb\"\n"));

        var run = await RunAsync(host, "project", "check", "acme/shop");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("validation: set -e\\nnpm ci", run.Output, StringComparison.Ordinal);
        Assert.Contains("validation: a\\tb", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hostile_value_in_a_setting_is_not_printed_as_it_is()
    {
        await using var host = await StartRegisteredAsync(GatewayWith("image: node:22\nvalidation: [x]\nlanguage: \"\\e[2J\\u202Ex\"\n"));

        var run = await RunAsync(host, "project", "check", "acme/shop");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("'?[2J?x' is not a language", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Output, c => c is '\u001b' or '\u202E');
    }

    [Fact]
    public async Task A_check_with_no_name_is_not_understood_and_help_names_the_command()
    {
        await using var host = await StartRegisteredAsync(GatewayWith(Valid));

        var missing = await RunAsync(host, "project", "check");
        var help = await RunAsync(host, "project", "--help");
        var checkHelp = await RunAsync(host, "project", "check", "--help");

        Assert.Equal(2, missing.ExitCode);
        Assert.Contains("check", help.Output, StringComparison.Ordinal);
        Assert.Equal(0, checkHelp.ExitCode);
        Assert.Contains("hephaestus.yml", checkHelp.Output, StringComparison.Ordinal);
        Assert.Contains("--json", checkHelp.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"repositoryId":1,"name":"acme/shop","baseBranch":"main","status":"valid","commitId":"abc","issues":[],"configuration":null}""")]
    [InlineData("""{"repositoryId":1,"name":"acme/shop","baseBranch":"main","status":"invalid","commitId":"abc","issues":[],"configuration":null}""")]
    [InlineData("""{"repositoryId":1,"name":"acme/shop","baseBranch":"main","status":"maybe","commitId":"abc","issues":[],"configuration":null}""")]
    [InlineData("""{"repositoryId":1,"name":"acme/shop","baseBranch":"main","status":"invalid","commitId":"abc","issues":[{"setting":"x","line":1,"id":"config.issue.nothing-like-it","arguments":[]}],"configuration":null}""")]
    [InlineData("""{"repositoryId":1,"name":"acme/shop","baseBranch":"main","status":"invalid","commitId":"abc","issues":[{"setting":"x","line":1,"id":"config.issue.required"}],"configuration":null}""")]
    [InlineData("""{"repositoryId":1,"name":"acme/shop","baseBranch":"main","status":"missing","issues":[],"configuration":null}""")]
    [InlineData("not json")]
    public async Task An_answer_that_is_not_a_whole_document_is_a_failure_and_prints_nothing_as_if_it_were_a_result(string body)
    {
        await using var stub = await StubApi.StartAsync(app => app.MapGet("/api/projects/check", () => Results.Text(body, "application/json")));

        var run = await CliRun.ExecuteAsync(stub.SocketPath, Language.English, "project", "check", "acme/shop");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.NotEmpty(run.ErrorLines);
    }

    [Fact]
    public async Task Json_output_is_printed_as_the_API_gave_it_and_the_exit_code_follows_its_status()
    {
        await using var stub = await StubApi.StartAsync(app => app.MapGet("/api/projects/check", () => Results.Text("""{"status":"valid"}""", "application/json")));

        var run = await CliRun.ExecuteAsync(stub.SocketPath, Language.English, "project", "check", "acme/shop", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("\"status\":\"valid\"", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_check_changes_nothing_about_the_Projects()
    {
        await using var host = await StartRegisteredAsync(GatewayWith("broken: ["));
        var before = (await RunAsync(host, "project", "list", "--all", "--json")).Output;

        await RunAsync(host, "project", "check", "acme/shop");

        Assert.Equal(before, (await RunAsync(host, "project", "list", "--all", "--json")).Output);
        Assert.Single(await host.App.Services.GetRequiredService<IReadStore>().ListProjectsAsync(includeRemoved: true, Token));
    }

    [Fact]
    public async Task Without_a_running_host_the_check_says_the_host_cannot_be_reached_and_exits_1()
    {
        using var socket = new TempSocket();

        var run = await CliRun.ExecuteAsync(socket.Path, Language.English, "project", "check", "acme/shop");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Contains(socket.Path, run.Error, StringComparison.Ordinal);
    }
}
