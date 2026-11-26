using System.Net;
using System.Security.Cryptography;
using System.Text;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Secrets;
using Hephaestus.GitHub;
using Hephaestus.Host.Tests.Cli;

namespace Hephaestus.Host.Tests.Api;

/// <summary>
/// Story 1.7 with the real stores and the real gateway, and only GitHub's answers made up: the key lands as a secret file and is masked,
/// the App id lands in the database, and a refused credential changes nothing.
/// </summary>
public class GitHubIdentityEndToEndTests
{
    private const string App = """
        {"id":123456,"slug":"hephaestus-test","name":"Hephaestus Test","owner":{"login":"adamcharusta","id":1,"type":"User"},
        "permissions":{"contents":"write","issues":"write","pull_requests":"write","metadata":"read","checks":"read","statuses":"read"}}
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task<RunningHost> StartAsync(GitHubAnswers github) =>
        RunningHost.StartAsync(configure: builder => builder.Services.AddSingleton<ICodeHost>(provider => new GitHubGateway(
            provider.GetRequiredService<ISecretStore>(),
            provider.GetRequiredService<IReadStore>(),
            provider.GetRequiredService<IInstallationConfiguration>(),
            TimeProvider.System,
            () => github)));

    private static string NewKey() => RSA.Create(2048).ExportRSAPrivateKeyPem();

    private static string KeyFile(RunningHost host) => Path.Combine(host.Settings.SecretsDirectory, GitHubAppCredential.PrivateKeySecretName);

    private static Task<CliRun> ConnectAsync(RunningHost host, string key, string appId = "123456") =>
        CliRun.ExecuteWithInputAsync(key + "\n", host.Settings.ControlSocketPath, Language.English, "github", "connect", "--app-id", appId);

    [Fact]
    public async Task Connecting_stores_the_key_as_a_secret_file_the_id_in_the_database_and_masks_the_key()
    {
        var github = new GitHubAnswers();
        await using var host = await StartAsync(github);
        var key = NewKey();

        var run = await ConnectAsync(host, key);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(key, await File.ReadAllTextAsync(KeyFile(host), Token));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyFile(host)));
        }

        Assert.Equal(123456, await host.App.Services.GetRequiredService<IReadStore>().GetGitHubAppIdAsync(Token));
        Assert.Contains(GitHubAppCredential.PrivateKeySecretName, await host.App.Services.GetRequiredService<IReadStore>().ListSecretNamesAsync(Token));
        var body = key.Split('\n')[1];
        Assert.Equal(SecretMasker.Replacement, host.App.Services.GetRequiredService<SecretMasker>().Mask(body));
        Assert.DoesNotContain(body, run.Output + run.Error, StringComparison.Ordinal);

        var secrets = await CliRun.ExecuteAsync(host.Settings.ControlSocketPath, Language.English, "secret", "list");
        Assert.Contains(GitHubAppCredential.PrivateKeySecretName, secrets.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(body, secrets.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checking_afterwards_reads_the_stored_credential_without_the_key_on_the_command_line()
    {
        var github = new GitHubAnswers();
        await using var host = await StartAsync(github);
        await ConnectAsync(host, NewKey());

        var run = await CliRun.ExecuteAsync(host.Settings.ControlSocketPath, Language.English, "github", "check");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("GitHub App: Hephaestus Test (hephaestus-test), id 123456, owned by adamcharusta", run.Output, StringComparison.Ordinal);
        Assert.EndsWith($"All required permissions are present.{Environment.NewLine}", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_credential_GitHub_refuses_changes_nothing_and_the_one_stored_before_still_works()
    {
        var github = new GitHubAnswers();
        await using var host = await StartAsync(github);
        var first = NewKey();
        await ConnectAsync(host, first);
        github.Status = HttpStatusCode.Unauthorized;

        var refused = await ConnectAsync(host, NewKey(), appId: "999");

        Assert.Equal(1, refused.ExitCode);
        Assert.Equal("GitHub credential rejected", refused.ErrorLines[0]);
        Assert.Equal(first, await File.ReadAllTextAsync(KeyFile(host), Token));
        Assert.Equal(123456, await host.App.Services.GetRequiredService<IReadStore>().GetGitHubAppIdAsync(Token));
        github.Status = HttpStatusCode.OK;
        Assert.Equal(0, (await CliRun.ExecuteAsync(host.Settings.ControlSocketPath, Language.English, "github", "check")).ExitCode);
    }

    [Fact]
    public async Task An_unreachable_GitHub_on_a_first_connect_stores_nothing()
    {
        var github = new GitHubAnswers { Status = HttpStatusCode.BadGateway };
        await using var host = await StartAsync(github);

        var run = await ConnectAsync(host, NewKey());

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("GitHub is unreachable", run.ErrorLines[0]);
        Assert.False(File.Exists(KeyFile(host)));
        Assert.Null(await host.App.Services.GetRequiredService<IReadStore>().GetGitHubAppIdAsync(Token));
        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListSecretNamesAsync(Token));
        var check = await CliRun.ExecuteAsync(host.Settings.ControlSocketPath, Language.English, "github", "check");
        Assert.Equal("No GitHub App is connected", check.ErrorLines[0]);
    }

    [Fact]
    public async Task A_file_that_is_not_a_private_key_is_refused_before_GitHub_is_asked()
    {
        var github = new GitHubAnswers();
        await using var host = await StartAsync(github);

        var run = await ConnectAsync(host, "this is not a private key file");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("GitHub credential rejected", run.ErrorLines[0]);
        Assert.Contains("GitHub was not asked", run.ErrorLines[1], StringComparison.Ordinal);
        Assert.Equal(0, github.Requests);
        Assert.False(File.Exists(KeyFile(host)));
    }

    /// <summary>GitHub, as far as the gateway asks it anything: one App with no installation, or the status a test chooses.</summary>
    private sealed class GitHubAnswers : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var body = request.RequestUri!.AbsolutePath == "/app" ? App : "[]";
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Status == HttpStatusCode.OK ? body : "{}", Encoding.UTF8, "application/json") });
        }
    }
}
