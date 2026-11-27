using Hephaestus.Core.Localization;
using Hephaestus.Core.Workspaces;
using Hephaestus.Host.Tests.Api;

namespace Hephaestus.Host.Tests.Cli;

/// <summary>Story 3.10 on the command line: <c>agent set-auth</c>, <c>agent check</c>, and <c>agent list</c>, the credential on standard input and nowhere else.</summary>
public class AgentCommandTests
{
    private const string Credential = "sk-ant-oat01-0123456789-the-token";
    private const string Ready = """{"type":"result","subtype":"success","is_error":false,"result":"pong"}""";
    private const string Failed = """{"type":"result","subtype":"error_during_execution","is_error":true}""";

    private static Task<CliRun> SetAuthAsync(RunningHost host, string input, Language language = Language.English, params string[] args) =>
        CliRun.ExecuteWithInputAsync(input, host.Settings.ControlSocketPath, language, ["agent", "set-auth", .. args]);

    private static Task<CliRun> RunAsync(RunningHost host, Language language, params string[] args) =>
        CliRun.ExecuteAsync(host.Settings.ControlSocketPath, language, args);

    private static string[] StoredSecrets(RunningHost host) => Directory.Exists(host.Settings.SecretsDirectory) ? Directory.GetFiles(host.Settings.SecretsDirectory) : [];

    private static string NewLine(params string[] lines) => string.Join(Environment.NewLine, lines) + Environment.NewLine;

    [Fact]
    public async Task Set_auth_reads_the_credential_from_standard_input_stores_it_and_names_the_secret_that_holds_it()
    {
        var (host, _) = await AgentEndpointsTests.StartAsync();
        await using var _1 = host;

        var run = await SetAuthAsync(host, Credential + "\n", Language.English, "claude-code", "--mode", "subscription");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(NewLine("claude-code signs in with subscription. Its credential is the secret claude-code-oauth-token."), run.Output);
        Assert.Equal(string.Empty, run.Error);
        Assert.Equal(Credential, await File.ReadAllTextAsync(Path.Combine(host.Settings.SecretsDirectory, "claude-code-oauth-token"), TestContext.Current.CancellationToken));
        Assert.DoesNotContain(Credential, run.Output + run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Set_auth_with_json_prints_the_document_of_the_Agent_and_nothing_else()
    {
        var (host, _) = await AgentEndpointsTests.StartAsync();
        await using var _1 = host;

        var run = await SetAuthAsync(host, Credential, Language.English, "claude-code", "--mode", "api-key", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Matches("""^\{"agent":"claude-code","mode":"api-key","chosenAt":"[^"]+","secret":"anthropic-api-key","credentialStored":true,"status":null,"checkedAt":null,"reason":null\}\r?\n$""", run.Output);
        Assert.Equal(string.Empty, run.Error);
    }

    [Fact]
    public async Task An_empty_standard_input_keeps_the_credential_that_is_stored()
    {
        var (host, _) = await AgentEndpointsTests.StartAsync();
        await using var _1 = host;
        await RunAsync(host, Language.English, "secret", "set", "anthropic-api-key");
        await CliRun.ExecuteWithInputAsync(Credential, host.Settings.ControlSocketPath, Language.English, "secret", "set", "anthropic-api-key");

        var run = await SetAuthAsync(host, string.Empty, Language.English, "claude-code", "--mode", "api-key");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(Credential, await File.ReadAllTextAsync(Path.Combine(host.Settings.SecretsDirectory, "anthropic-api-key"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Set_auth_with_no_credential_and_none_stored_fails_and_says_where_one_would_be_stored()
    {
        var (host, _) = await AgentEndpointsTests.StartAsync();
        await using var _1 = host;

        var run = await SetAuthAsync(host, string.Empty, Language.English, "claude-code", "--mode", "api-key");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Equal("A credential is required", run.ErrorLines[0]);
        Assert.Contains("anthropic-api-key", run.ErrorLines[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gemini", "api-key", "The Agent is not known")]
    [InlineData("claude-code", "oauth", "The authentication mode is not known")]
    [InlineData("codex-cli", "api-key", "This Agent cannot be set up yet")]
    public async Task Set_auth_that_the_API_refuses_fails_with_the_problem_and_stores_nothing(string agent, string mode, string title)
    {
        var (host, _) = await AgentEndpointsTests.StartAsync();
        await using var _1 = host;

        var run = await SetAuthAsync(host, Credential, Language.English, agent, "--mode", mode);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(title, run.ErrorLines[0]);
        Assert.Empty(StoredSecrets(host));
    }

    [Theory]
    [InlineData("claude-code", "--mode", "api-key", "--value", Credential)]
    [InlineData("claude-code", "--mode", "api-key", Credential)]
    [InlineData("claude-code", Credential)]
    [InlineData("--mode", "api-key")]
    public async Task Set_auth_given_anything_it_does_not_understand_refuses_without_echoing_a_token_and_stores_nothing(params string[] args)
    {
        var (host, _) = await AgentEndpointsTests.StartAsync();
        await using var _1 = host;

        var run = await SetAuthAsync(host, Credential, Language.English, args);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("The arguments of agent set-auth were not understood", run.ErrorLines[0]);
        Assert.DoesNotContain(Credential, run.Output + run.Error, StringComparison.Ordinal);
        Assert.Empty(StoredSecrets(host));
    }

    [Fact]
    public async Task Set_auth_help_is_answered_even_when_the_mode_is_missing()
    {
        var (host, _) = await AgentEndpointsTests.StartAsync();
        await using var _1 = host;

        var run = await RunAsync(host, Language.English, "agent", "set-auth", "--help");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("--mode", run.Output, StringComparison.Ordinal);
        Assert.Contains("standard input", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_shows_each_Agent_with_its_mode_credential_and_last_check_and_none_where_nothing_is_known()
    {
        var (host, _) = await AgentEndpointsTests.StartAsync(_ => new ProbeResult(1, StepExitCause.Exited, [Failed]));
        await using var _1 = host;
        await SetAuthAsync(host, Credential, Language.English, "claude-code", "--mode", "api-key");
        await RunAsync(host, Language.English, "agent", "check", "claude-code");

        var run = await RunAsync(host, Language.English, "agent", "list");

        Assert.Equal(0, run.ExitCode);
        var lines = run.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Matches(@"^Agent\s+Mode\s+Credential\s+Status\s+Checked\s+Reason$", lines[0]);
        Assert.Matches(@"^claude-code\s+api-key\s+stored\s+unavailable\s+\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ\s+The CLI of claude-code reported an error and ended with exit code 1\.", lines[1]);
        Assert.Matches(@"^codex-cli\s+none\s+none\s+none\s+none\s+none$", lines[2]);
    }

    [Fact]
    public async Task List_with_json_is_the_document_of_the_API()
    {
        var (host, _) = await AgentEndpointsTests.StartAsync();
        await using var _1 = host;

        var run = await RunAsync(host, Language.English, "agent", "list", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.StartsWith("""{"agents":[{"agent":"claude-code","mode":null""", run.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, run.Error);
    }

    [Fact]
    public async Task Check_of_an_Agent_that_is_ready_says_so_and_ends_with_zero()
    {
        var (host, _) = await AgentEndpointsTests.StartAsync();
        await using var _1 = host;
        await SetAuthAsync(host, Credential, Language.English, "claude-code", "--mode", "subscription");

        var run = await RunAsync(host, Language.English, "agent", "check", "claude-code");

        Assert.Equal(0, run.ExitCode);
        Assert.Matches(@"^claude-code is ready, as of its probe at \d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ\.\r?\n$", run.Output);
        Assert.Equal(string.Empty, run.Error);
    }

    [Fact]
    public async Task Check_of_an_Agent_that_is_unavailable_prints_the_reason_on_standard_output_and_ends_with_one()
    {
        var (host, _) = await AgentEndpointsTests.StartAsync(_ => new ProbeResult(1, StepExitCause.Exited, [Failed]));
        await using var _1 = host;
        await SetAuthAsync(host, Credential, Language.English, "claude-code", "--mode", "api-key");

        var run = await RunAsync(host, Language.English, "agent", "check", "claude-code");

        Assert.Equal(1, run.ExitCode);
        Assert.Matches(@"^claude-code is unavailable, as of its probe at \d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ: The CLI of claude-code reported an error and ended with exit code 1\. ", run.Output);
        Assert.Equal(string.Empty, run.Error);
    }

    [Fact]
    public async Task Check_with_json_prints_the_document_and_the_exit_code_still_says_whether_the_Agent_is_ready()
    {
        var (host, _) = await AgentEndpointsTests.StartAsync(_ => new ProbeResult(1, StepExitCause.Exited, [Failed]));
        await using var _1 = host;
        await SetAuthAsync(host, Credential, Language.English, "claude-code", "--mode", "api-key");

        var run = await RunAsync(host, Language.English, "agent", "check", "claude-code", "--json");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("\"status\":\"unavailable\"", run.Output, StringComparison.Ordinal);
        Assert.Contains("\"reason\":{\"id\":\"agent.probe.error\",\"arguments\":[\"claude-code\",\"1\"]}", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_of_an_Agent_with_no_chosen_mode_fails_with_the_problem()
    {
        var (host, _) = await AgentEndpointsTests.StartAsync();
        await using var _1 = host;

        var run = await RunAsync(host, Language.English, "agent", "check", "claude-code");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("The Agent has no authentication mode", run.ErrorLines[0]);
        Assert.Equal(string.Empty, run.Output);
    }

    [Fact]
    public async Task The_commands_write_in_the_language_of_the_installation_and_the_names_stay_as_they_are()
    {
        var (host, _) = await AgentEndpointsTests.StartAsync();
        await using var _1 = host;
        var set = await SetAuthAsync(host, Credential, Language.Polish, "claude-code", "--mode", "subscription");
        var kept = await SetAuthAsync(host, string.Empty, Language.Polish, "claude-code", "--mode", "subscription");
        var check = await RunAsync(host, Language.Polish, "agent", "check", "claude-code");
        var list = await RunAsync(host, Language.Polish, "agent", "list");

        Assert.Equal(NewLine("Agent claude-code loguje się trybem subscription. Jego poświadczenie to sekret claude-code-oauth-token."), set.Output);
        Assert.Equal(NewLine("Agent claude-code loguje się trybem subscription. Jego poświadczenie to sekret claude-code-oauth-token, który był już zapisany i został zachowany."), kept.Output);
        Assert.Matches(@"^Agent claude-code jest gotowy według sondy z \d{4}", check.Output);
        Assert.Matches(@"^Agent\s+Tryb\s+Poświadczenie\s+Stan\s+Sprawdzono\s+Powód", list.Output.Split(Environment.NewLine)[0]);
        Assert.Contains("zapisane", list.Output, StringComparison.Ordinal);
        Assert.Contains("ready", list.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_Agent_that_is_unavailable_is_explained_in_the_language_of_the_installation_from_the_stored_reason()
    {
        var (host, _) = await AgentEndpointsTests.StartAsync(_ => new ProbeResult(1, StepExitCause.Exited, [Failed]));
        await using var _1 = host;
        await SetAuthAsync(host, Credential, Language.English, "claude-code", "--mode", "api-key");
        await RunAsync(host, Language.English, "agent", "check", "claude-code");

        var polish = await RunAsync(host, Language.Polish, "agent", "list");
        var english = await RunAsync(host, Language.English, "agent", "list");

        Assert.Contains("CLI agenta claude-code zgłosiło błąd i zakończyło się kodem 1.", polish.Output, StringComparison.Ordinal);
        Assert.Contains("The CLI of claude-code reported an error and ended with exit code 1.", english.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Choosing_a_mode_with_an_empty_input_says_the_credential_was_kept_and_not_that_it_was_stored()
    {
        var (host, _) = await AgentEndpointsTests.StartAsync();
        await using var _1 = host;
        await SetAuthAsync(host, Credential, Language.English, "claude-code", "--mode", "api-key");

        var kept = await SetAuthAsync(host, "\n", Language.English, "claude-code", "--mode", "api-key");

        Assert.Equal(0, kept.ExitCode);
        Assert.Equal(NewLine("claude-code signs in with api-key. Its credential is the secret anthropic-api-key, which was already stored and was kept."), kept.Output);
    }

    [Fact]
    public async Task Without_a_host_the_commands_fail_with_the_message_every_command_gives()
    {
        using var socket = new TempSocket();

        var run = await CliRun.ExecuteAsync(socket.Path, Language.English, "agent", "list");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.NotEmpty(run.Error);
    }
}
