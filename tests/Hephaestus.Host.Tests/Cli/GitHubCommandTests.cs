using Hephaestus.Core.GitHub;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;

namespace Hephaestus.Host.Tests.Cli;

/// <summary>Story 1.7 on the command line: <c>github connect --app-id</c> and <c>github check</c>, the key on standard input and nowhere else.</summary>
public class GitHubCommandTests
{
    private const string Key = "-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEAu1SU1LfVLPHCozMxH2Mo4lgOEePzNm0tRgeLezV6ffAt0gun\n-----END RSA PRIVATE KEY-----";

    private static Task<RunningHost> StartAsync(FakeCodeHost gateway, Language language = Language.English) =>
        RunningHost.StartAsync(language, configure: builder => builder.Services.AddSingleton<ICodeHost>(gateway));

    private static Task<CliRun> ConnectAsync(RunningHost host, string input, Language language = Language.English, params string[] more) =>
        CliRun.ExecuteWithInputAsync(input, host.Settings.ControlSocketPath, language, ["github", "connect", "--app-id", "123456", .. more]);

    private static Task<CliRun> RunAsync(RunningHost host, Language language, params string[] args) =>
        CliRun.ExecuteAsync(host.Settings.ControlSocketPath, language, args);

    [Fact]
    public async Task Connect_reads_the_key_from_standard_input_and_reports_the_App_where_it_is_installed_and_each_permission()
    {
        var gateway = new FakeCodeHost();
        await using var host = await StartAsync(gateway);

        var run = await ConnectAsync(host, Key + "\n");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        Assert.Equal([(123456L, Key)], gateway.Connects);
        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "Connected the GitHub App.",
                "GitHub App: Hephaestus Test (hephaestus-test), id 123456, owned by adamcharusta",
                string.Empty,
                "Installed on:",
                "  acme (Organization): all repositories",
                "    acme/api",
                "    acme/web",
                "  adamcharusta (User): selected repositories",
                "    no repositories selected",
                "  old-org (Organization): suspended; its repositories are not listed",
                string.Empty,
                "Permissions:",
                "  Permission     Needs  Has    Status",
                "  contents       write  write  present",
                "  issues         write  write  present",
                "  pull-requests  write  read   missing",
                "  metadata       read   read   present",
                "  checks         read   none   missing",
                "  statuses       read   read   present",
                string.Empty,
                "Warning: the App is granted workflows at level write. Hephaestus never needs it. Remove it in the App's settings on GitHub.",
                string.Empty,
                "Some required permissions are missing. Grant them in the App's settings on GitHub, then run github check.",
                string.Empty),
            run.Output);
        Assert.DoesNotContain("MIIE", run.Output + run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_reports_the_same_without_saying_it_connected()
    {
        var gateway = new FakeCodeHost();
        await using var host = await StartAsync(gateway);

        var run = await RunAsync(host, Language.English, "github", "check");

        Assert.Equal(0, run.ExitCode);
        Assert.StartsWith("GitHub App: Hephaestus Test", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Connected the GitHub App.", run.Output, StringComparison.Ordinal);
        Assert.Equal(1, gateway.Checks);
    }

    [Fact]
    public async Task An_App_with_every_permission_and_no_installation_is_told_to_install_it()
    {
        var identity = FakeCodeHost.Identity with
        {
            Installations = [],
            Permissions = [.. FakeCodeHost.Identity.Permissions.Select(p => p with { Granted = p.Required, Present = true })],
            Warnings = [],
        };
        var gateway = new FakeCodeHost { Answer = GitHubIdentityResult.Succeeded(identity) };
        await using var host = await StartAsync(gateway);

        var run = await RunAsync(host, Language.English, "github", "check");

        Assert.Contains("Not installed anywhere yet. Install the App on the repositories Hephaestus is to work on.", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Warning", run.Output, StringComparison.Ordinal);
        Assert.EndsWith($"All required permissions are present.{Environment.NewLine}", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Polish_changes_the_sentences_and_the_headers_and_nothing_a_machine_reads()
    {
        var gateway = new FakeCodeHost();
        await using var host = await StartAsync(gateway, Language.Polish);

        var run = await ConnectAsync(host, Key, Language.Polish);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Połączono aplikację GitHub.", run.Output, StringComparison.Ordinal);
        Assert.Contains("Uprawnienie", run.Output, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^  pull-requests +write +read +brak$", run.Output);
        Assert.Matches(@"(?m)^  contents +write +write +jest$", run.Output);
        Assert.Contains("  acme (Organization): wszystkie repozytoria", run.Output, StringComparison.Ordinal);
        Assert.Contains("    acme/api", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_prints_the_document_of_the_API_and_nothing_else_for_both_commands()
    {
        var gateway = new FakeCodeHost();
        await using var host = await StartAsync(gateway);

        var connect = await ConnectAsync(host, Key, Language.English, "--json");
        var check = await RunAsync(host, Language.Polish, "github", "check", "--json");

        foreach (var run in new[] { connect, check })
        {
            Assert.Equal(0, run.ExitCode);
            Assert.Equal(string.Empty, run.Error);
            Assert.StartsWith("""{"appId":123456,"slug":"hephaestus-test",""", run.Output, StringComparison.Ordinal);
            Assert.Single(run.Output.TrimEnd().Split('\n'));
        }

        Assert.Equal(connect.Output, check.Output);
    }

    [Theory]
    [InlineData(GitHubIdentityFailure.NotConnected, "No GitHub App is connected")]
    [InlineData(GitHubIdentityFailure.Rejected, "GitHub credential rejected")]
    [InlineData(GitHubIdentityFailure.KeyUnreadable, "GitHub credential rejected")]
    [InlineData(GitHubIdentityFailure.Unreachable, "GitHub is unreachable")]
    public async Task A_failure_goes_to_standard_error_as_title_then_detail_with_exit_code_1(GitHubIdentityFailure failure, string title)
    {
        var gateway = new FakeCodeHost { Answer = GitHubIdentityResult.Failed(failure) };
        await using var host = await StartAsync(gateway);

        var connect = await ConnectAsync(host, Key);
        var check = await RunAsync(host, Language.English, "github", "check");

        foreach (var run in new[] { connect, check })
        {
            Assert.Equal(1, run.ExitCode);
            Assert.Equal(string.Empty, run.Output);
            Assert.Equal(title, run.ErrorLines[0]);
            Assert.Contains("Nothing was changed.", run.ErrorLines[1], StringComparison.Ordinal);
            Assert.DoesNotContain("MIIE", run.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task An_empty_standard_input_is_refused_before_GitHub_is_asked()
    {
        var gateway = new FakeCodeHost();
        await using var host = await StartAsync(gateway);

        var run = await ConnectAsync(host, string.Empty);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("Empty secret value", run.ErrorLines[0]);
        Assert.Empty(gateway.Connects);
    }

    [Fact]
    public async Task A_key_given_on_the_command_line_is_refused_without_repeating_it()
    {
        var gateway = new FakeCodeHost();
        await using var host = await StartAsync(gateway);
        const string Token = "MIIEowIBAAKCAQEAu1SU1LfVLPHC";

        var runs = new[]
        {
            await RunAsync(host, Language.English, "github", "connect", "--app-id", "123456", Token),
            await RunAsync(host, Language.English, "github", "connect", "--app-id", Token),
            await RunAsync(host, Language.English, "github", "connect", "--app-id", "123456", $"--private-key={Token}"),
            await RunAsync(host, Language.English, "github", "connect", $"--app-id={Token}"),
            await RunAsync(host, Language.English, "github", "connect", Token),
            await RunAsync(host, Language.English, "github", "connect", "--app-id"),
            await RunAsync(host, Language.English, "github", "connect"),
        };

        foreach (var run in runs)
        {
            Assert.Equal(2, run.ExitCode);
            Assert.Equal(string.Empty, run.Output);
            Assert.Equal("The command was not understood, and the private key cannot be given on the command line", run.ErrorLines[0]);
            Assert.StartsWith("Nothing was stored. Give only --app-id", run.ErrorLines[1], StringComparison.Ordinal);
            Assert.DoesNotContain(Token, run.Error, StringComparison.Ordinal);
        }

        Assert.Empty(gateway.Connects);
    }

    [Theory]
    [InlineData("github", "connect", "--help", "MIIEowIBAAKCAQEAu1SU1LfVLPHC")]
    [InlineData("github", "connect", "MIIEowIBAAKCAQEAu1SU1LfVLPHC", "--help")]
    public async Task A_key_beside_help_is_not_repeated_either(params string[] args)
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var run = await RunAsync(host, Language.English, args);

        Assert.Equal(2, run.ExitCode);
        Assert.DoesNotContain("MIIEowIBAAKCAQEAu1SU1LfVLPHC", run.Output + run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_the_command_cannot_read_is_a_failure_and_prints_no_report()
    {
        foreach (var body in new[] { "not json", "{}", """{"appId":1,"installations":null,"permissions":[],"warnings":[]}""" })
        {
            await using var api = await StubApi.StartAsync(app => app.MapGet("/api/github/identity", () => Results.Text(body, "application/json")));

            var run = await CliRun.ExecuteAsync(api.SocketPath, Language.English, "github", "check");

            Assert.Equal(1, run.ExitCode);
            Assert.Equal(string.Empty, run.Output);
            Assert.Equal("Unexpected answer", run.ErrorLines[0]);
        }
    }

    [Fact]
    public async Task Help_names_the_commands_and_the_option_and_works_without_an_app_id()
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var root = await RunAsync(host, Language.English, "--help");
        var group = await RunAsync(host, Language.English, "github", "--help");
        var connect = await RunAsync(host, Language.English, "github", "connect", "--help");
        var bare = await RunAsync(host, Language.English, "github");

        Assert.Contains("  github          Connects Hephaestus's own GitHub App and checks what it may do.", root.Output, StringComparison.Ordinal);
        Assert.Contains("  connect  Connects the GitHub App; its private key is read from standard input.", group.Output, StringComparison.Ordinal);
        Assert.Contains("  check    Reads the App's identity, installations, and permissions from GitHub.", group.Output, StringComparison.Ordinal);
        Assert.Contains("--app-id", connect.Output, StringComparison.Ordinal);
        Assert.Equal(0, connect.ExitCode);
        Assert.Equal(group.Output, bare.Output);
    }

    [Fact]
    public async Task A_host_that_does_not_answer_is_reported_like_any_other_command()
    {
        using var socket = new TempSocket();

        var run = await CliRun.ExecuteWithInputAsync(Key, socket.Path, Language.English, "github", "connect", "--app-id", "1");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.NotEmpty(run.ErrorLines);
    }
}
