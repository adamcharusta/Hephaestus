using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;

namespace Hephaestus.Host.Tests.Cli;

/// <summary>Story 1.10 on the command line: <c>project add</c>, <c>list</c>, and <c>remove</c>.</summary>
public class ProjectCommandTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task<RunningHost> StartAsync(FakeCodeHost gateway, Language language = Language.English, TempSocket? installation = null) =>
        RunningHost.StartAsync(language, configure: builder => builder.Services.AddSingleton<ICodeHost>(gateway), installation: installation);

    private static Task<CliRun> RunAsync(RunningHost host, Language language, params string[] args) =>
        CliRun.ExecuteAsync(host.Settings.ControlSocketPath, language, args);

    private static Task<CliRun> RunAsync(RunningHost host, params string[] args) => RunAsync(host, Language.English, args);

    private static string Line(string text) => text + Environment.NewLine;

    [Fact]
    public async Task Add_registers_the_Project_and_says_the_Base_Branch_is_protected()
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var run = await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "main");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(Line("Registered Project acme/shop (GitHub repository id 101) with Base Branch main.") + Line("GitHub reports Base Branch main as protected."), run.Output);
        Assert.Equal(string.Empty, run.Error);
        var stored = Assert.Single(await host.App.Services.GetRequiredService<IReadStore>().ListProjectsAsync(includeRemoved: true, Token));
        Assert.Equal((101L, "acme/shop", "main"), (stored.RepositoryId, stored.FullName, stored.BaseBranch));
    }

    [Fact]
    public async Task Add_recommends_protection_when_the_Base_Branch_is_not_protected_and_still_registers_it()
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var run = await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "develop");

        Assert.Equal(0, run.ExitCode);
        var lines = run.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Registered Project acme/shop (GitHub repository id 101) with Base Branch develop.", lines[0]);
        Assert.StartsWith("GitHub reports Base Branch develop as not protected. Protect it against direct pushes on GitHub", lines[1], StringComparison.Ordinal);
        Assert.Single(await host.App.Services.GetRequiredService<IReadStore>().ListProjectsAsync(includeRemoved: true, Token));
    }

    [Fact]
    public async Task A_repository_without_the_App_fails_and_says_the_App_is_not_installed()
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var run = await RunAsync(host, "project", "add", "acme/other", "--base-branch", "main");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Equal(2, run.ErrorLines.Length);
        Assert.Equal("The GitHub App is not installed on the repository", run.ErrorLines[0]);
        Assert.StartsWith("The Hephaestus GitHub App is not installed on acme/other, or the repository does not exist.", run.ErrorLines[1], StringComparison.Ordinal);
        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListProjectsAsync(includeRemoved: true, Token));
    }

    [Fact]
    public async Task A_Base_Branch_that_does_not_exist_fails_and_says_the_branch_is_wrong()
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var run = await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "nope");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(["The Base Branch does not exist", "acme/shop has no branch named nope. Nothing was changed."], run.ErrorLines);
        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListProjectsAsync(includeRemoved: true, Token));
    }

    [Fact]
    public async Task Without_a_Base_Branch_the_command_is_not_understood_and_nothing_is_stored()
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var run = await RunAsync(host, "project", "add", "acme/shop");

        Assert.Equal(2, run.ExitCode);
        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListProjectsAsync(includeRemoved: true, Token));
    }

    [Fact]
    public async Task A_name_that_is_not_owner_slash_repository_is_refused_with_exit_1_and_GitHub_is_not_asked()
    {
        var gateway = new FakeCodeHost();
        await using var host = await StartAsync(gateway);

        var run = await RunAsync(host, "project", "add", "shop", "--base-branch", "main");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("Invalid repository name", run.ErrorLines[0]);
        Assert.Empty(gateway.RepositoryLookups);
    }

    [Fact]
    public async Task Without_a_connected_GitHub_identity_add_says_to_connect_one()
    {
        await using var host = await StartAsync(new FakeCodeHost { RepositoryLookupFails = GitHubRepositoryFailure.NotConnected });

        var run = await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "main");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("No GitHub App is connected", run.ErrorLines[0]);
        Assert.Contains("github connect", run.ErrorLines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adding_the_same_Project_again_says_so_and_exits_0()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "main");

        var again = await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "main");

        Assert.Equal(0, again.ExitCode);
        Assert.Equal(Line("acme/shop (GitHub repository id 101) is already registered with Base Branch main. Nothing was changed.") + Line("GitHub reports Base Branch main as protected."), again.Output);
        Assert.Single(await host.App.Services.GetRequiredService<IReadStore>().ListProjectsAsync(includeRemoved: true, Token));
    }

    [Fact]
    public async Task Adding_a_registered_Project_with_another_Base_Branch_changes_it()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "main");

        var again = await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "develop");

        Assert.Equal(0, again.ExitCode);
        Assert.StartsWith(Line("Updated Project acme/shop (GitHub repository id 101): its Base Branch is develop."), again.Output, StringComparison.Ordinal);
        Assert.Equal("develop", Assert.Single(await host.App.Services.GetRequiredService<IReadStore>().ListProjectsAsync(includeRemoved: false, Token)).BaseBranch);
    }

    [Fact]
    public async Task List_shows_the_name_the_id_the_Base_Branch_and_when_it_was_registered()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "main");
        await RunAsync(host, "project", "add", "acme/api", "--base-branch", "main");

        var list = await RunAsync(host, "project", "list");

        Assert.Equal(0, list.ExitCode);
        var lines = list.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("Project    GitHub repository id  Base Branch  Registered", lines[0]);
        Assert.Matches(@"^acme/api   102                   main         \d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", lines[1]);
        Assert.StartsWith("acme/shop  101 ", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_Project_the_list_says_so_and_names_the_command_that_registers_one()
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var list = await RunAsync(host, "project", "list");
        var json = await RunAsync(host, "project", "list", "--json");

        Assert.Equal(Line("No Projects. Register one with: project add <owner>/<repo> --base-branch <branch>"), list.Output);
        Assert.Equal(Line("""{"projects":[]}"""), json.Output);
    }

    [Fact]
    public async Task A_renamed_repository_is_still_listed_with_the_same_id_and_its_new_name_after_it_is_registered_again()
    {
        var gateway = new FakeCodeHost();
        await using var host = await StartAsync(gateway);
        await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "main");
        gateway.Repositories.Remove("acme/shop");
        gateway.Repositories["acme/store"] = (101, "acme/store", new Dictionary<string, bool> { ["main"] = true });

        var before = await RunAsync(host, "project", "list", "--json");
        var again = await RunAsync(host, "project", "add", "acme/store", "--base-branch", "main");
        var after = await RunAsync(host, "project", "list", "--json");

        Assert.Contains("\"repositoryId\":101,\"name\":\"acme/shop\"", before.Output, StringComparison.Ordinal);
        Assert.StartsWith("Updated Project acme/store (GitHub repository id 101)", again.Output, StringComparison.Ordinal);
        Assert.Contains("\"repositoryId\":101,\"name\":\"acme/store\"", after.Output, StringComparison.Ordinal);
        Assert.Equal(1, after.Output.Split("\"repositoryId\"").Length - 1);
    }

    [Fact]
    public async Task Remove_says_what_was_removed_and_that_the_record_is_kept_and_the_list_omits_it()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "main");
        await RunAsync(host, "project", "add", "acme/api", "--base-branch", "main");

        var remove = await RunAsync(host, "project", "remove", "acme/shop");
        var list = await RunAsync(host, "project", "list");

        Assert.Equal(0, remove.ExitCode);
        Assert.Equal(Line("Removed Project acme/shop (GitHub repository id 101). Its record and everything recorded under it are kept."), remove.Output);
        Assert.DoesNotContain("acme/shop", list.Output, StringComparison.Ordinal);
        Assert.Contains("acme/api", list.Output, StringComparison.Ordinal);
        Assert.Single(await host.App.Services.GetRequiredService<IReadStore>().ListProjectsAsync(includeRemoved: false, Token));
        Assert.Equal(2, (await host.App.Services.GetRequiredService<IReadStore>().ListProjectsAsync(includeRemoved: true, Token)).Count);
    }

    [Fact]
    public async Task List_all_shows_a_removed_Project_with_when_it_was_removed()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "main");
        await RunAsync(host, "project", "remove", "acme/shop");

        var active = await RunAsync(host, "project", "list");
        var all = await RunAsync(host, "project", "list", "--all");

        Assert.Equal(Line("No Projects. Register one with: project add <owner>/<repo> --base-branch <branch>"), active.Output);
        var lines = all.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith("Registered            Removed", lines[0], StringComparison.Ordinal);
        Assert.Matches(@"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ  \d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", lines[1]);
    }

    [Fact]
    public async Task A_removed_Project_can_be_registered_again()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "main");
        await RunAsync(host, "project", "remove", "acme/shop");

        var again = await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "main");
        var list = await RunAsync(host, "project", "list");

        Assert.StartsWith("Registered Project acme/shop", again.Output, StringComparison.Ordinal);
        Assert.Contains("acme/shop", list.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Removing_a_name_that_is_not_registered_fails_with_exit_1()
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var run = await RunAsync(host, "project", "remove", "acme/shop");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(["Project not found", "acme/shop is not a registered Project. Nothing was changed. Run project list to see which are."], run.ErrorLines);
    }

    [Fact]
    public async Task The_list_is_unchanged_when_the_host_starts_again_on_the_same_volumes()
    {
        using var installation = new TempSocket();
        string before;
        await using (var first = await StartAsync(new FakeCodeHost(), installation: installation))
        {
            await RunAsync(first, "project", "add", "acme/shop", "--base-branch", "main");
            await RunAsync(first, "project", "add", "acme/api", "--base-branch", "main");
            before = (await RunAsync(first, "project", "list", "--json")).Output;
        }

        await using var second = await StartAsync(new FakeCodeHost(), installation: installation);
        var after = await RunAsync(second, "project", "list", "--json");

        Assert.Equal(before, after.Output);
        Assert.Contains("\"repositoryId\":102", after.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_output_is_the_document_of_the_API_and_is_not_translated()
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var english = await RunAsync(host, Language.English, "project", "add", "acme/shop", "--base-branch", "main", "--json");
        var polish = await RunAsync(host, Language.Polish, "project", "add", "acme/shop", "--base-branch", "main", "--json");

        Assert.Matches("""^\{"repositoryId":101,"name":"acme/shop","baseBranch":"main","registeredAt":"[^"]+","removedAt":null,"change":"registered","baseBranchProtected":true\}\r?\n$""", english.Output);
        Assert.Contains("\"change\":\"unchanged\"", polish.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_sentences_are_polish_and_the_command_names_and_ids_are_not()
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var add = await RunAsync(host, Language.Polish, "project", "add", "acme/shop", "--base-branch", "develop");
        var again = await RunAsync(host, Language.Polish, "project", "add", "acme/shop", "--base-branch", "develop");
        var list = await RunAsync(host, Language.Polish, "project", "list");
        var remove = await RunAsync(host, Language.Polish, "project", "remove", "acme/shop");
        var empty = await RunAsync(host, Language.Polish, "project", "list");
        var failure = await RunAsync(host, Language.Polish, "project", "add", "acme/shop", "--base-branch", "nope");
        var protectedBranch = await RunAsync(host, Language.Polish, "project", "add", "acme/shop", "--base-branch", "main");

        Assert.StartsWith(Line("Zarejestrowano Projekt acme/shop (identyfikator repozytorium GitHuba 101) z gałęzią bazową develop."), add.Output, StringComparison.Ordinal);
        Assert.Contains("GitHub zgłasza gałąź bazową develop jako niechronioną.", add.Output, StringComparison.Ordinal);
        Assert.Contains("jest już zarejestrowany", again.Output, StringComparison.Ordinal);
        Assert.Matches(@"^Projekt +Identyfikator repozytorium GitHuba +Gałąź bazowa +Zarejestrowano", list.Output);
        Assert.StartsWith("Usunięto Projekt acme/shop", remove.Output, StringComparison.Ordinal);
        Assert.StartsWith("Brak Projektów.", empty.Output, StringComparison.Ordinal);
        Assert.Equal("Gałąź bazowa nie istnieje", failure.ErrorLines[0]);
        Assert.Equal(1, failure.ExitCode);
        Assert.Contains("GitHub zgłasza gałąź bazową main jako chronioną.", protectedBranch.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Help_names_the_three_commands_and_the_Base_Branch_option()
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var project = await RunAsync(host, "project", "--help");
        var add = await RunAsync(host, "project", "add", "--help");

        Assert.Equal(0, project.ExitCode);
        foreach (var verb in new[] { "add", "list", "remove" })
        {
            Assert.Contains(verb, project.Output, StringComparison.Ordinal);
        }

        Assert.Contains("--base-branch", add.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_root_help_lists_project()
    {
        await using var host = await StartAsync(new FakeCodeHost());

        var root = await RunAsync(host, "--help");

        Assert.Contains("project", root.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("../secrets/token")]
    [InlineData("acme/..")]
    [InlineData("acme/../secrets/token")]
    public async Task A_name_with_dot_segments_is_refused_as_a_name_and_reaches_no_other_endpoint(string name)
    {
        await using var host = await StartAsync(new FakeCodeHost());
        await CliRun.ExecuteWithInputAsync("a-secret-value", host.Settings.ControlSocketPath, Language.English, "secret", "set", "token");

        var remove = await RunAsync(host, "project", "remove", name);
        var add = await RunAsync(host, "project", "add", name, "--base-branch", "main");

        Assert.Equal([1, 1], new[] { remove.ExitCode, add.ExitCode });
        Assert.Equal("Invalid repository name", remove.ErrorLines[0]);
        Assert.Equal("Invalid repository name", add.ErrorLines[0]);
        Assert.Equal(["token"], await host.App.Services.GetRequiredService<IReadStore>().ListSecretNamesAsync(Token));
    }

    [Fact]
    public async Task List_all_has_the_Removed_column_even_when_no_Project_was_removed()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "main");

        var all = await RunAsync(host, "project", "list", "--all");
        var active = await RunAsync(host, "project", "list");

        Assert.Matches(@"^Project +GitHub repository id +Base Branch +Registered +Removed", all.Output);
        Assert.Matches(@"none\s*$", all.Output.TrimEnd());
        Assert.DoesNotContain("Removed", active.Output, StringComparison.Ordinal);
    }
}
