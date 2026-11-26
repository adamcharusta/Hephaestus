using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Trust;

namespace Hephaestus.Host.Tests.Cli;

/// <summary>Story 1.8 on the command line: <c>trusted-user add</c>, <c>list</c>, and <c>remove</c>.</summary>
public class TrustedUserCommandTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task<RunningHost> StartAsync(FakeCodeHost gateway, Language language = Language.English, TempSocket? installation = null) =>
        RunningHost.StartAsync(language, configure: builder => builder.Services.AddSingleton<ICodeHost>(gateway), installation: installation);

    private static Task<CliRun> RunAsync(RunningHost host, Language language, params string[] args) =>
        CliRun.ExecuteAsync(host.Settings.ControlSocketPath, language, args);

    private static Task<CliRun> RunAsync(RunningHost host, params string[] args) => RunAsync(host, Language.English, args);

    [Fact]
    public async Task Add_says_who_was_added_by_login_and_id_and_the_user_is_listed()
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var add = await RunAsync(host, "trusted-user", "add", "alice");
        var list = await RunAsync(host, "trusted-user", "list");

        Assert.Equal(0, add.ExitCode);
        Assert.Equal($"Added Trusted User Alice (GitHub user id 583231).{Environment.NewLine}", add.Output);
        Assert.Equal(string.Empty, add.Error);
        Assert.Equal(0, list.ExitCode);
        var lines = list.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Matches(@"^Login  GitHub user id  Added$", lines[0]);
        Assert.Matches(@"^Alice  583231          \d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", lines[1]);
    }

    [Fact]
    public async Task Adding_the_same_user_again_says_so_exits_0_and_lists_one_row()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        await RunAsync(host, "trusted-user", "add", "alice");

        var again = await RunAsync(host, "trusted-user", "add", "alice");

        Assert.Equal(0, again.ExitCode);
        Assert.Equal($"Alice (GitHub user id 583231) is already a Trusted User. Nothing was added.{Environment.NewLine}", again.Output);
        Assert.Single(await host.App.Services.GetRequiredService<IReadStore>().ListTrustedUsersAsync(Token));
    }

    [Fact]
    public async Task A_user_who_changed_their_login_is_listed_under_the_same_id()
    {
        var gateway = new FakeCodeHost();
        await using var host = await StartAsync(gateway);
        await RunAsync(host, "trusted-user", "add", "alice");
        gateway.Users["alice"] = new GitHubUser(583231, "alice-renamed", IsHephaestus: false);

        var before = await RunAsync(host, "trusted-user", "list", "--json");
        var again = await RunAsync(host, "trusted-user", "add", "alice");
        var after = await RunAsync(host, "trusted-user", "list", "--json");

        Assert.Contains("\"login\":\"Alice\"", before.Output, StringComparison.Ordinal);
        Assert.Equal(0, again.ExitCode);
        Assert.Contains("\"userId\":583231", after.Output, StringComparison.Ordinal);
        Assert.Contains("\"login\":\"alice-renamed\"", after.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Alice\"", after.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_login_that_does_not_exist_fails_with_exit_1_and_says_the_user_was_not_found()
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var run = await RunAsync(host, "trusted-user", "add", "nobody");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Equal(["GitHub user not found", "GitHub has no user with the login nobody. Nothing was changed."], run.ErrorLines);
        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListTrustedUsersAsync(Token));
    }

    [Fact]
    public async Task Hephaestuss_own_identity_is_refused_with_exit_1()
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var run = await RunAsync(host, "trusted-user", "add", "hephaestus-test[bot]");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("Hephaestus cannot trust itself", run.ErrorLines[0]);
        Assert.Contains("never a Trusted User", run.ErrorLines[1], StringComparison.Ordinal);
        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListTrustedUsersAsync(Token));
    }

    [Fact]
    public async Task Without_a_connected_GitHub_identity_add_says_to_connect_one()
    {
        await using var host = await StartAsync(new FakeCodeHost { LookupFails = GitHubUserFailure.NotConnected });

        var run = await RunAsync(host, "trusted-user", "add", "alice");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("No GitHub App is connected", run.ErrorLines[0]);
        Assert.Contains("github connect", run.ErrorLines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remove_says_who_was_removed_and_a_later_list_omits_them()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        await RunAsync(host, "trusted-user", "add", "alice");
        await RunAsync(host, "trusted-user", "add", "bob");

        var remove = await RunAsync(host, "trusted-user", "remove", "alice");
        var list = await RunAsync(host, "trusted-user", "list");

        Assert.Equal(0, remove.ExitCode);
        Assert.Equal($"Removed Trusted User Alice (GitHub user id 583231).{Environment.NewLine}", remove.Output);
        Assert.DoesNotContain("Alice", list.Output, StringComparison.Ordinal);
        Assert.Contains("bob", list.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Removing_the_last_user_leaves_a_list_that_says_there_are_none()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        await RunAsync(host, "trusted-user", "add", "alice");
        await RunAsync(host, "trusted-user", "remove", "alice");

        var list = await RunAsync(host, "trusted-user", "list");
        var json = await RunAsync(host, "trusted-user", "list", "--json");

        Assert.Equal($"No Trusted Users.{Environment.NewLine}", list.Output);
        Assert.Equal($"{{\"trustedUsers\":[]}}{Environment.NewLine}", json.Output);
    }

    [Fact]
    public async Task Removing_someone_who_is_not_trusted_fails_with_exit_1()
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var run = await RunAsync(host, "trusted-user", "remove", "nobody");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(["Trusted User not found", "nobody is not a Trusted User. Nothing was changed. Run trusted-user list to see who is."], run.ErrorLines);
    }

    [Fact]
    public async Task The_list_is_unchanged_when_the_host_starts_again_on_the_same_volumes()
    {
        using var installation = new TempSocket();
        string before;
        await using (var first = await StartAsync(new FakeCodeHost(), installation: installation))
        {
            await RunAsync(first, "trusted-user", "add", "alice");
            await RunAsync(first, "trusted-user", "add", "bob");
            before = (await RunAsync(first, "trusted-user", "list", "--json")).Output;
        }

        await using var second = await StartAsync(new FakeCodeHost(), installation: installation);
        var after = await RunAsync(second, "trusted-user", "list", "--json");

        Assert.Equal(before, after.Output);
        Assert.Contains("\"userId\":1001", after.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_output_is_the_document_of_the_API_and_is_not_translated()
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var english = await RunAsync(host, Language.English, "trusted-user", "add", "alice", "--json");
        var polish = await RunAsync(host, Language.Polish, "trusted-user", "add", "alice", "--json");
        var list = await RunAsync(host, Language.Polish, "trusted-user", "list", "--json");

        Assert.Matches("""^\{"userId":583231,"login":"Alice","addedAt":"[^"]+"\}\r?\n$""", english.Output);
        Assert.Equal(english.Output, polish.Output);
        Assert.StartsWith("{\"trustedUsers\":[{\"userId\":583231", list.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_sentences_are_polish_and_the_command_names_and_ids_are_not()
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var add = await RunAsync(host, Language.Polish, "trusted-user", "add", "alice");
        var again = await RunAsync(host, Language.Polish, "trusted-user", "add", "alice");
        var list = await RunAsync(host, Language.Polish, "trusted-user", "list");
        var remove = await RunAsync(host, Language.Polish, "trusted-user", "remove", "alice");
        var empty = await RunAsync(host, Language.Polish, "trusted-user", "list");
        var failure = await RunAsync(host, Language.Polish, "trusted-user", "add", "nobody");

        Assert.Equal($"Dodano zaufanego użytkownika Alice (identyfikator użytkownika GitHuba 583231).{Environment.NewLine}", add.Output);
        Assert.Contains("jest już zaufanym użytkownikiem", again.Output, StringComparison.Ordinal);
        Assert.StartsWith("Login  Identyfikator użytkownika GitHuba  Dodano", list.Output, StringComparison.Ordinal);
        Assert.Equal($"Usunięto zaufanego użytkownika Alice (identyfikator użytkownika GitHuba 583231).{Environment.NewLine}", remove.Output);
        Assert.Equal($"Brak zaufanych użytkowników.{Environment.NewLine}", empty.Output);
        Assert.Equal("Nie znaleziono użytkownika GitHuba", failure.ErrorLines[0]);
        Assert.Equal(1, failure.ExitCode);
    }

    [Fact]
    public async Task Help_lists_the_three_commands_and_each_has_its_own_help()
    {
        using var socket = new TempSocket();

        var group = await CliRun.ExecuteAsync(socket.Path, Language.English, "trusted-user", "--help");
        var add = await CliRun.ExecuteAsync(socket.Path, Language.English, "trusted-user", "add", "--help");
        var root = await CliRun.ExecuteAsync(socket.Path, Language.English, "--help");

        Assert.StartsWith("Usage: hephaestus trusted-user <command> [options]", group.Output, StringComparison.Ordinal);
        Assert.Contains("  add     ", group.Output, StringComparison.Ordinal);
        Assert.Contains("  list    ", group.Output, StringComparison.Ordinal);
        Assert.Contains("  remove  ", group.Output, StringComparison.Ordinal);
        Assert.StartsWith("Usage: hephaestus trusted-user add <login> [options]", add.Output, StringComparison.Ordinal);
        Assert.Contains("  trusted-user  Manages the GitHub users Hephaestus acts on.", root.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("trusted-user", "add")]
    [InlineData("trusted-user", "remove")]
    [InlineData("trusted-user", "add", "alice", "bob")]
    [InlineData("trusted-user", "list", "alice")]
    [InlineData("trusted-user", "grant", "alice")]
    public async Task A_command_that_is_not_understood_exits_2_and_asks_nothing_of_the_host(params string[] args)
    {
        using var socket = new TempSocket();

        var run = await CliRun.ExecuteAsync(socket.Path, Language.English, args);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
    }

    [Fact]
    public async Task With_no_host_listening_the_command_says_so_and_exits_1()
    {
        using var socket = new TempSocket();

        var run = await CliRun.ExecuteAsync(socket.Path, Language.English, "trusted-user", "list");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"userId":1}""")]
    [InlineData("""{"trustedUsers":null}""")]
    [InlineData("""{"trustedUsers":[{"userId":1}]}""")]
    [InlineData("""{"trustedUsers":[null]}""")]
    public async Task An_answer_the_command_cannot_read_is_a_failure_and_prints_nothing(string body)
    {
        await using var api = await StubApi.StartAsync(app =>
        {
            app.MapGet("/api/trusted-users", () => Results.Text(body, "application/json"));
            app.MapPut("/api/trusted-users/{login}", (string login) => Results.Text(body, "application/json", statusCode: 201));
            app.MapDelete("/api/trusted-users/{login}", (string login) => Results.Text(body, "application/json"));
        });

        foreach (var args in new[] { new[] { "trusted-user", "list" }, ["trusted-user", "add", "alice"], ["trusted-user", "remove", "alice"] })
        {
            var run = await CliRun.ExecuteAsync(api.SocketPath, Language.English, args);

            Assert.Equal(1, run.ExitCode);
            Assert.Equal(string.Empty, run.Output);
            Assert.Equal("Unexpected answer", run.ErrorLines[0]);
        }
    }

    [Fact]
    public async Task A_login_with_a_bracket_reaches_the_gateway_as_typed()
    {
        var gateway = new FakeCodeHost();
        await using var host = await StartAsync(gateway);

        await RunAsync(host, "trusted-user", "add", "hephaestus-test[bot]");

        Assert.Equal(["hephaestus-test[bot]"], gateway.Lookups);
    }
}
