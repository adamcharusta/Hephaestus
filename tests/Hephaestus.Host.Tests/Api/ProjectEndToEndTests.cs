using System.Net;
using System.Security.Cryptography;
using System.Text;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.GitHub;
using Hephaestus.Host.Tests.Cli;

namespace Hephaestus.Host.Tests.Api;

/// <summary>
/// Story 1.10 with the real stores and the real gateway, and only GitHub's answers made up: a repository becomes GitHub's numeric id in
/// the database, a renamed repository stays one Project, and a repository or branch GitHub does not show is refused.
/// </summary>
public class ProjectEndToEndTests
{
    private const string App = """
        {"id":123456,"slug":"hephaestus-test","name":"Hephaestus Test","owner":{"login":"adamcharusta","id":1,"type":"User"},
        "permissions":{"contents":"write","issues":"write","pull_requests":"write","metadata":"read","checks":"read","statuses":"read"}}
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task<RunningHost> StartAsync(GitHubAnswers github, TempSocket? installation = null) =>
        RunningHost.StartAsync(
            configure: builder => builder.Services.AddSingleton<ICodeHost>(provider => new GitHubGateway(
                provider.GetRequiredService<ISecretStore>(),
                provider.GetRequiredService<IReadStore>(),
                provider.GetRequiredService<IInstallationConfiguration>(),
                TimeProvider.System,
                () => github)),
            installation: installation);

    private static async Task ConnectAsync(RunningHost host)
    {
        var run = await CliRun.ExecuteWithInputAsync(RSA.Create(2048).ExportRSAPrivateKeyPem() + "\n", host.Settings.ControlSocketPath, Language.English, "github", "connect", "--app-id", "123456");
        Assert.Equal(0, run.ExitCode);
    }

    private static Task<CliRun> RunAsync(RunningHost host, params string[] args) => CliRun.ExecuteAsync(host.Settings.ControlSocketPath, Language.English, args);

    [Fact]
    public async Task Registering_finds_the_repository_through_GitHub_and_stores_its_id_with_the_Base_Branch()
    {
        var github = new GitHubAnswers();
        await using var host = await StartAsync(github);
        await ConnectAsync(host);

        var run = await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "main");

        Assert.Equal(0, run.ExitCode);
        Assert.StartsWith($"Registered Project acme/shop (GitHub repository id 4242) with Base Branch main.{Environment.NewLine}GitHub reports Base Branch main as protected.", run.Output, StringComparison.Ordinal);
        var stored = Assert.Single(await host.App.Services.GetRequiredService<IReadStore>().ListProjectsAsync(includeRemoved: true, Token));
        Assert.Equal((4242L, "acme/shop", "main"), (stored.RepositoryId, stored.FullName, stored.BaseBranch));
    }

    [Fact]
    public async Task A_repository_that_was_renamed_is_still_listed_and_is_removed_by_its_new_name_with_its_record_kept()
    {
        var github = new GitHubAnswers();
        await using var host = await StartAsync(github);
        await ConnectAsync(host);
        await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "main");
        github.Rename("acme/shop", "acme/store");

        var list = await RunAsync(host, "project", "list");
        var remove = await RunAsync(host, "project", "remove", "acme/store");

        Assert.Contains("4242", list.Output, StringComparison.Ordinal);
        Assert.Equal(0, remove.ExitCode);
        Assert.StartsWith("Removed Project acme/shop (GitHub repository id 4242).", remove.Output, StringComparison.Ordinal);
        var projects = await host.App.Services.GetRequiredService<IReadStore>().ListProjectsAsync(includeRemoved: true, Token);
        Assert.Equal(4242, Assert.Single(projects).RepositoryId);
        Assert.False(projects[0].IsActive);
    }

    [Fact]
    public async Task A_repository_without_the_App_and_a_branch_that_does_not_exist_are_told_apart_and_nothing_is_stored()
    {
        await using var host = await StartAsync(new GitHubAnswers());
        await ConnectAsync(host);

        var notInstalled = await RunAsync(host, "project", "add", "acme/other", "--base-branch", "main");
        var noBranch = await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "nope");

        Assert.Equal("The GitHub App is not installed on the repository", notInstalled.ErrorLines[0]);
        Assert.Equal("The Base Branch does not exist", noBranch.ErrorLines[0]);
        Assert.Equal([1, 1], new[] { notInstalled.ExitCode, noBranch.ExitCode });
        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListProjectsAsync(includeRemoved: true, Token));
    }

    [Fact]
    public async Task Without_a_connected_App_nothing_is_asked_of_GitHub_and_the_command_says_to_connect()
    {
        var github = new GitHubAnswers();
        await using var host = await StartAsync(github);

        var run = await RunAsync(host, "project", "add", "acme/shop", "--base-branch", "main");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("No GitHub App is connected", run.ErrorLines[0]);
        Assert.Empty(github.Paths);
    }

    [Fact]
    public async Task The_Projects_are_still_listed_after_the_host_restarts_even_when_GitHub_is_gone()
    {
        using var installation = new TempSocket();
        var github = new GitHubAnswers();
        await using (var first = await StartAsync(github, installation))
        {
            await ConnectAsync(first);
            await RunAsync(first, "project", "add", "acme/shop", "--base-branch", "main");
        }

        github.Down = true;
        await using var second = await StartAsync(github, installation);
        var list = await RunAsync(second, "project", "list");
        var remove = await RunAsync(second, "project", "remove", "acme/shop");

        Assert.Contains("4242", list.Output, StringComparison.Ordinal);
        Assert.Equal(0, remove.ExitCode);
    }

    /// <summary>GitHub, as far as the gateway asks it anything: the App, one installation on <c>acme</c>, its repositories, and their branches.</summary>
    private sealed class GitHubAnswers : HttpMessageHandler
    {
        private string _repository = "acme/shop";

        public List<string> Paths { get; } = [];

        public bool Down { get; set; }

        public void Rename(string from, string to)
        {
            Assert.Equal(from, _repository);
            _repository = to;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            Paths.Add(path);
            if (Down)
            {
                return Task.FromResult(Answer(HttpStatusCode.BadGateway, "{}"));
            }

            return Task.FromResult(path switch
            {
                "/app" => Answer(HttpStatusCode.OK, App),
                "/app/installations" => Answer(HttpStatusCode.OK, """[{"id":10,"account":{"login":"acme","id":2,"type":"Organization"},"target_type":"Organization","repository_selection":"all"}]"""),
                "/app/installations/10/access_tokens" => Answer(HttpStatusCode.Created, """{"token":"ghs_token-0123456789","expires_at":"2099-01-01T00:00:00Z"}"""),
                "/installation/repositories" => Answer(HttpStatusCode.OK, $$"""{"total_count":1,"repositories":[{"id":4242,"full_name":"{{_repository}}","name":"{{_repository.Split('/')[1]}}"}]}"""),
                "/repositories/4242/branches/main" => Answer(HttpStatusCode.OK, """{"name":"main","protected":true,"commit":{"sha":"abc","url":"https://api.github.com/x"}}"""),
                _ => Answer(HttpStatusCode.NotFound, """{"message":"Not Found"}"""),
            });
        }

        private static HttpResponseMessage Answer(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
