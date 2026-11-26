using System.Net;
using System.Security.Cryptography;
using System.Text;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.GitHub;
using Hephaestus.Host.Tests.Cli;

namespace Hephaestus.Host.Tests.Api;

/// <summary>
/// Story 1.8 with the real stores and the real gateway, and only GitHub's answers made up: a login becomes GitHub's numeric id in the
/// database, a renamed user stays one user, and the App's own bot account is refused.
/// </summary>
public class TrustedUserEndToEndTests
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
    public async Task Adding_resolves_the_login_through_GitHub_and_stores_the_id()
    {
        var github = new GitHubAnswers();
        await using var host = await StartAsync(github);
        await ConnectAsync(host);

        var run = await RunAsync(host, "trusted-user", "add", "alice");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal($"Added Trusted User Alice (GitHub user id 583231).{Environment.NewLine}", run.Output);
        var stored = Assert.Single(await host.App.Services.GetRequiredService<IReadStore>().ListTrustedUsersAsync(Token));
        Assert.Equal((583231L, "Alice"), (stored.UserId, stored.Login));
        Assert.Contains("/users/alice", github.Paths);
    }

    [Fact]
    public async Task A_user_who_changed_their_login_is_still_listed_and_is_removed_by_the_new_login_too()
    {
        var github = new GitHubAnswers();
        await using var host = await StartAsync(github);
        await ConnectAsync(host);
        await RunAsync(host, "trusted-user", "add", "alice");
        github.Users["alice-renamed"] = """{"login":"alice-renamed","id":583231,"type":"User"}""";
        github.Users.Remove("alice");

        var list = await RunAsync(host, "trusted-user", "list");
        var remove = await RunAsync(host, "trusted-user", "remove", "alice-renamed");

        Assert.Contains("583231", list.Output, StringComparison.Ordinal);
        Assert.Equal(0, remove.ExitCode);
        Assert.Equal($"Removed Trusted User Alice (GitHub user id 583231).{Environment.NewLine}", remove.Output);
        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListTrustedUsersAsync(Token));
    }

    [Fact]
    public async Task A_login_GitHub_does_not_know_and_the_Apps_own_bot_are_refused_and_nothing_is_stored()
    {
        await using var host = await StartAsync(new GitHubAnswers());
        await ConnectAsync(host);

        var unknown = await RunAsync(host, "trusted-user", "add", "nobody");
        var own = await RunAsync(host, "trusted-user", "add", "hephaestus-test[bot]");

        Assert.Equal("GitHub user not found", unknown.ErrorLines[0]);
        Assert.Equal("Hephaestus cannot trust itself", own.ErrorLines[0]);
        Assert.Equal([1, 1], new[] { unknown.ExitCode, own.ExitCode });
        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListTrustedUsersAsync(Token));
    }

    [Fact]
    public async Task Without_a_connected_App_nothing_is_asked_of_GitHub_and_the_command_says_to_connect()
    {
        var github = new GitHubAnswers();
        await using var host = await StartAsync(github);

        var run = await RunAsync(host, "trusted-user", "add", "alice");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("No GitHub App is connected", run.ErrorLines[0]);
        Assert.Empty(github.Paths);
    }

    [Fact]
    public async Task The_users_are_still_listed_after_the_host_restarts_even_when_GitHub_is_gone()
    {
        using var installation = new TempSocket();
        var github = new GitHubAnswers();
        await using (var first = await StartAsync(github, installation))
        {
            await ConnectAsync(first);
            await RunAsync(first, "trusted-user", "add", "alice");
        }

        github.Down = true;
        await using var second = await StartAsync(github, installation);
        var list = await RunAsync(second, "trusted-user", "list");
        var remove = await RunAsync(second, "trusted-user", "remove", "alice");

        Assert.Contains("583231", list.Output, StringComparison.Ordinal);
        Assert.Equal(0, remove.ExitCode);
    }

    /// <summary>GitHub, as far as the gateway asks it anything: the App, no installation, and the users a test lists.</summary>
    private sealed class GitHubAnswers : HttpMessageHandler
    {
        public Dictionary<string, string> Users { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["alice"] = """{"login":"Alice","id":583231,"type":"User"}""",
            ["hephaestus-test[bot]"] = """{"login":"hephaestus-test[bot]","id":99999,"type":"Bot"}""",
        };

        public List<string> Paths { get; } = [];

        public bool Down { get; set; }

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
                "/app/installations" => Answer(HttpStatusCode.OK, "[]"),
                _ when path.StartsWith("/users/", StringComparison.Ordinal) && Users.TryGetValue(path["/users/".Length..], out var user) => Answer(HttpStatusCode.OK, user),
                _ => Answer(HttpStatusCode.NotFound, """{"message":"Not Found"}"""),
            });
        }

        private static HttpResponseMessage Answer(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
