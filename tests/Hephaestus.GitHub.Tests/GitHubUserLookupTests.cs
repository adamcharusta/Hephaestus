using System.Net;
using System.Security.Cryptography;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Trust;

namespace Hephaestus.GitHub.Tests;

/// <summary>Story 1.8, AD-15: the gateway resolves a login to GitHub's numeric user id as the connected App, and knows its own bot account.</summary>
public class GitHubUserLookupTests
{
    private const long AppId = 123456;
    private const string KeyName = GitHubAppCredential.PrivateKeySecretName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FakeStores Connected() => new() { AppId = AppId, Secrets = { [KeyName] = RSA.Create(2048).ExportRSAPrivateKeyPem() } };

    private static GitHubGateway GatewayOver(FakeStores stores, FakeGitHub github, TimeSpan? deadline = null) =>
        new(stores, stores, stores, new FixedTime(new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero)), () => github, deadline);

    [Fact]
    public async Task A_login_resolves_to_the_numeric_id_and_the_login_GitHub_spells()
    {
        var result = await GatewayOver(Connected(), new FakeGitHub()).FindUserAsync("alice", Token);

        Assert.Equal(GitHubUserFailure.None, result.Failure);
        Assert.Equal(new GitHubUser(583231, "Alice", IsHephaestus: false), result.User);
    }

    [Fact]
    public async Task The_lookup_is_made_as_the_App_with_an_installation_token_and_only_reads()
    {
        var github = new FakeGitHub();

        await GatewayOver(Connected(), github).FindUserAsync("alice", Token);

        Assert.StartsWith("Bearer ", github.Requests.Single(r => r.Path == "/app").Authorization, StringComparison.Ordinal);
        Assert.EndsWith(FakeGitHub.InstallationToken, github.Requests.Single(r => r.Path == "/users/alice").Authorization, StringComparison.Ordinal);
        Assert.All(github.Requests, r => Assert.Contains(r.Method.Method, new[] { "GET", "POST" }));
        Assert.DoesNotContain(github.Requests, r => r.Path == "/installation/repositories");
    }

    [Fact]
    public async Task With_no_live_installation_the_lookup_is_made_without_a_token()
    {
        var github = new FakeGitHub
        {
            InstallationsJson = $$"""[{"id":10,"account":{"login":"acme","id":2,"type":"Organization"},"target_type":"Organization","repository_selection":"all","suspended_at":"2026-11-01T00:00:00Z","permissions":{{FakeGitHub.Permissions}}}]""",
        };

        var result = await GatewayOver(Connected(), github).FindUserAsync("alice", Token);

        Assert.Equal(583231, result.User!.Id);
        Assert.Null(github.Requests.Single(r => r.Path == "/users/alice").Authorization);
        Assert.DoesNotContain(github.Requests, r => r.Path.EndsWith("/access_tokens", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_Apps_own_bot_account_is_recognised_whatever_the_case_of_the_login_typed()
    {
        var github = new FakeGitHub();

        var result = await GatewayOver(Connected(), github).FindUserAsync("Hephaestus-Test[bot]", Token);

        Assert.True(result.User!.IsHephaestus);
        Assert.Equal(99999, result.User.Id);
    }

    [Fact]
    public async Task Another_bot_is_a_user_that_is_not_Hephaestus()
    {
        var github = new FakeGitHub();
        github.Users["dependabot[bot]"] = """{"login":"dependabot[bot]","id":49699333,"type":"Bot"}""";

        var result = await GatewayOver(Connected(), github).FindUserAsync("dependabot[bot]", Token);

        Assert.False(result.User!.IsHephaestus);
    }

    [Fact]
    public async Task A_login_GitHub_has_no_user_for_is_not_found()
    {
        var result = await GatewayOver(Connected(), new FakeGitHub()).FindUserAsync("nobody", Token);

        Assert.Equal(GitHubUserFailure.NotFound, result.Failure);
        Assert.Null(result.User);
    }

    [Fact]
    public async Task An_organization_is_not_a_user_that_can_be_trusted()
    {
        var result = await GatewayOver(Connected(), new FakeGitHub()).FindUserAsync("acme", Token);

        Assert.Equal(GitHubUserFailure.NotFound, result.Failure);
    }

    [Fact]
    public async Task Nothing_stored_means_not_connected_and_GitHub_is_not_asked()
    {
        var github = new FakeGitHub();

        Assert.Equal(GitHubUserFailure.NotConnected, (await GatewayOver(new FakeStores(), github).FindUserAsync("alice", Token)).Failure);
        Assert.Equal(GitHubUserFailure.NotConnected, (await GatewayOver(new FakeStores { AppId = AppId }, github).FindUserAsync("alice", Token)).Failure);
        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task A_stored_key_that_is_not_a_key_is_unreadable_and_GitHub_is_not_asked()
    {
        var github = new FakeGitHub();
        var stores = new FakeStores { AppId = AppId, Secrets = { [KeyName] = "not a pem key at all" } };

        Assert.Equal(GitHubUserFailure.KeyUnreadable, (await GatewayOver(stores, github).FindUserAsync("alice", Token)).Failure);
        Assert.Empty(github.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_credential_GitHub_refuses_is_rejected(HttpStatusCode status)
    {
        var result = await GatewayOver(Connected(), new FakeGitHub { AppStatus = status }).FindUserAsync("alice", Token);

        Assert.Equal(GitHubUserFailure.Rejected, result.Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task GitHub_failing_to_serve_the_lookup_is_unreachable(HttpStatusCode status)
    {
        var github = new FakeGitHub
        {
            Override = request => request.RequestUri!.AbsolutePath.StartsWith("/users/", StringComparison.Ordinal) ? FakeGitHub.Json(status, """{"message":"x"}""") : null,
        };

        Assert.Equal(GitHubUserFailure.Unreachable, (await GatewayOver(Connected(), github).FindUserAsync("alice", Token)).Failure);
    }

    [Fact]
    public async Task A_lookup_that_outlasts_its_deadline_is_unreachable()
    {
        var result = await GatewayOver(Connected(), new FakeGitHub { Hang = true }, TimeSpan.FromMilliseconds(200)).FindUserAsync("alice", Token);

        Assert.Equal(GitHubUserFailure.Unreachable, result.Failure);
    }

    [Fact]
    public async Task A_lookup_cancelled_by_the_caller_is_cancelled_not_reported_as_unreachable()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GatewayOver(Connected(), new FakeGitHub()).FindUserAsync("alice", cancelled.Token));
    }

    [Fact]
    public async Task A_lookup_stores_nothing()
    {
        var stores = Connected();
        var before = stores.Secrets.ToDictionary(p => p.Key, p => p.Value);

        await GatewayOver(stores, new FakeGitHub()).FindUserAsync("alice", Token);

        Assert.Equal(before, stores.Secrets);
        Assert.Equal(AppId, stores.AppId);
        Assert.Empty(stores.TrustedUsers);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
