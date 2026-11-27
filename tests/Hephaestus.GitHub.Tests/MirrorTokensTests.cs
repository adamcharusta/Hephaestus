using System.Net;
using System.Security.Cryptography;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Projects;

namespace Hephaestus.GitHub.Tests;

/// <summary>Story 3.5, AD-4: the installation token that reads a repository is found the way the poller finds it, and every way it cannot be found is a failure the loop can tell apart.</summary>
public sealed class MirrorTokensTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly FakeStores _stores = new() { AppId = 123456, Secrets = { [GitHubAppCredential.PrivateKeySecretName] = RSA.Create(2048).ExportRSAPrivateKeyPem() } };
    private readonly FakeGitHub _github = new();
    private readonly MovingTime _time = new(Now);
    private readonly GitHubRateLimit _limit;

    public MirrorTokensTests()
    {
        _limit = new GitHubRateLimit(_time);
        _stores.Projects.Add(new Project(1, "acme/web", "main", Now, null));
    }

    private MirrorTokens Tokens() => new(_stores, _stores, _time, _limit, () => _github);

    [Fact]
    public async Task The_installation_that_holds_the_repository_gives_its_token()
    {
        var token = await Tokens().TokenAsync(1, Cancel);

        Assert.Equal(MirrorFailure.None, token.Failure);
        Assert.Equal(FakeGitHub.InstallationToken, token.Token);
    }

    [Fact]
    public async Task A_token_is_kept_for_the_next_request_and_not_asked_for_again()
    {
        var tokens = Tokens();
        await tokens.TokenAsync(1, Cancel);
        var asked = _github.Requests.Count(r => r.Path.EndsWith("/access_tokens", StringComparison.Ordinal));

        await tokens.TokenAsync(2, Cancel);

        Assert.Equal(asked, _github.Requests.Count(r => r.Path.EndsWith("/access_tokens", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_repository_no_installation_holds_is_not_installed()
    {
        var token = await Tokens().TokenAsync(99, Cancel);

        Assert.Equal(MirrorFailure.NotInstalled, token.Failure);
        Assert.Null(token.Token);
    }

    [Fact]
    public async Task No_stored_app_id_means_not_connected_and_GitHub_is_not_asked()
    {
        _stores.AppId = null;

        Assert.Equal(MirrorFailure.NotConnected, (await Tokens().TokenAsync(1, Cancel)).Failure);
        Assert.Empty(_github.Requests);
    }

    [Fact]
    public async Task No_stored_key_means_not_connected()
    {
        _stores.Secrets.Clear();

        Assert.Equal(MirrorFailure.NotConnected, (await Tokens().TokenAsync(1, Cancel)).Failure);
    }

    [Fact]
    public async Task A_key_that_is_not_a_pem_private_key_is_unreadable()
    {
        _stores.Secrets[GitHubAppCredential.PrivateKeySecretName] = "not a key";

        Assert.Equal(MirrorFailure.KeyUnreadable, (await Tokens().TokenAsync(1, Cancel)).Failure);
        Assert.Empty(_github.Requests);
    }

    [Fact]
    public async Task A_closed_rate_limit_gate_is_unreachable_and_nothing_is_sent()
    {
        _limit.CloseUntil(Now.AddMinutes(10));

        Assert.Equal(MirrorFailure.Unreachable, (await Tokens().TokenAsync(1, Cancel)).Failure);
        Assert.Empty(_github.Requests);
    }

    [Fact]
    public async Task A_server_error_is_unreachable()
    {
        _github.Override = _ => FakeGitHub.Json(HttpStatusCode.ServiceUnavailable, """{"message":"unavailable"}""");

        Assert.Equal(MirrorFailure.Unreachable, (await Tokens().TokenAsync(1, Cancel)).Failure);
    }

    [Fact]
    public async Task A_rate_limit_answer_closes_the_gate_for_everyone()
    {
        _github.Override = _ =>
        {
            var response = FakeGitHub.Json(HttpStatusCode.TooManyRequests, """{"message":"slow down"}""");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(5));
            return response;
        };

        Assert.Equal(MirrorFailure.Unreachable, (await Tokens().TokenAsync(1, Cancel)).Failure);
        Assert.NotNull(_limit.ClosedUntil);
    }

    [Fact]
    public async Task A_credential_GitHub_refuses_is_rejected_and_the_tokens_are_forgotten()
    {
        _github.Override = _ => FakeGitHub.Json(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""");
        var tokens = Tokens();

        Assert.Equal(MirrorFailure.Rejected, (await tokens.TokenAsync(1, Cancel)).Failure);

        _github.Override = null;
        Assert.Equal(FakeGitHub.InstallationToken, (await tokens.TokenAsync(1, Cancel)).Token);
    }

    [Fact]
    public async Task Forgetting_makes_the_next_request_ask_GitHub_again()
    {
        var tokens = Tokens();
        await tokens.TokenAsync(1, Cancel);
        var asked = _github.Requests.Count(r => r.Path.EndsWith("/access_tokens", StringComparison.Ordinal));

        tokens.Forget();
        await tokens.TokenAsync(1, Cancel);

        Assert.True(_github.Requests.Count(r => r.Path.EndsWith("/access_tokens", StringComparison.Ordinal)) > asked);
    }

    [Fact]
    public void The_public_constructor_builds_the_token_lookup_from_the_stores()
    {
        var mirrors = new GitHubMirrors(_stores, _stores, _time, _limit, new GitMirrorOptions("/tmp/hephaestus-never-used", "/tmp/hephaestus-never-used-bundles"), () => _github);

        Assert.NotNull(mirrors);
    }
}
