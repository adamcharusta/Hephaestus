using System.Net;
using System.Security.Cryptography;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Projects;

namespace Hephaestus.GitHub.Tests;

/// <summary>Story 1.10, AD-4, AD-19: the gateway finds a repository the connected App is installed on, and reads whether a branch exists and is protected.</summary>
public class GitHubRepositoryLookupTests
{
    private const long AppId = 123456;
    private const string KeyName = GitHubAppCredential.PrivateKeySecretName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FakeStores Connected() => new() { AppId = AppId, Secrets = { [KeyName] = RSA.Create(2048).ExportRSAPrivateKeyPem() } };

    private static GitHubGateway GatewayOver(FakeStores stores, FakeGitHub github, TimeSpan? deadline = null) =>
        new(stores, stores, stores, new FixedTime(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero)), () => github, deadline);

    [Fact]
    public async Task A_repository_the_App_is_installed_on_is_found_with_its_id_and_its_protected_Base_Branch()
    {
        var result = await GatewayOver(Connected(), new FakeGitHub()).FindRepositoryAsync("acme/web", "main", Token);

        Assert.Equal(GitHubRepositoryFailure.None, result.Failure);
        Assert.Equal(new GitHubRepository(1, "acme/web", new GitHubBranch("main", IsProtected: true)), result.Repository);
    }

    [Fact]
    public async Task A_branch_GitHub_reports_as_not_protected_is_reported_so()
    {
        var result = await GatewayOver(Connected(), new FakeGitHub()).FindRepositoryAsync("acme/api", "main", Token);

        Assert.Equal(new GitHubRepository(2, "acme/api", new GitHubBranch("main", IsProtected: false)), result.Repository);
    }

    [Fact]
    public async Task The_name_is_found_whatever_its_case_and_comes_back_as_GitHub_spells_it()
    {
        var result = await GatewayOver(Connected(), new FakeGitHub()).FindRepositoryAsync("ACME/Web", "main", Token);

        Assert.Equal("acme/web", result.Repository!.FullName);
    }

    [Fact]
    public async Task A_branch_with_a_slash_is_read()
    {
        var result = await GatewayOver(Connected(), new FakeGitHub()).FindRepositoryAsync("acme/web", "release/1.0", Token);

        Assert.Equal(new GitHubBranch("release/1.0", IsProtected: true), result.Repository!.Branch);
    }

    [Theory]
    [InlineData("a#b", false)]
    [InlineData("a+b", true)]
    public async Task A_branch_with_a_character_that_means_something_in_an_address_is_read_as_that_branch(string branch, bool isProtected)
    {
        var result = await GatewayOver(Connected(), new FakeGitHub()).FindRepositoryAsync("acme/web", branch, Token);

        Assert.Equal(new GitHubBranch(branch, isProtected), result.Repository!.Branch);
    }

    [Fact]
    public async Task Without_a_branch_the_repository_is_found_and_no_branch_is_read()
    {
        var github = new FakeGitHub();

        var result = await GatewayOver(Connected(), github).FindRepositoryAsync("acme/api", branch: null, Token);

        Assert.Equal(new GitHubRepository(2, "acme/api", Branch: null), result.Repository);
        Assert.DoesNotContain(github.Requests, r => r.Path.Contains("/branches/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_branch_the_repository_does_not_have_is_not_found_while_the_repository_is()
    {
        var result = await GatewayOver(Connected(), new FakeGitHub()).FindRepositoryAsync("acme/web", "nope", Token);

        Assert.Equal(GitHubRepositoryFailure.BranchNotFound, result.Failure);
        Assert.Null(result.Repository);
    }

    [Theory]
    [InlineData("acme/unknown")]
    [InlineData("stranger/web")]
    [InlineData("old-org/anything")]
    [InlineData("web")]
    public async Task A_repository_the_App_is_not_installed_on_is_not_installed_whether_or_not_it_exists(string name)
    {
        var result = await GatewayOver(Connected(), new FakeGitHub()).FindRepositoryAsync(name, "main", Token);

        Assert.Equal(GitHubRepositoryFailure.NotInstalled, result.Failure);
    }

    [Fact]
    public async Task A_suspended_installation_reaches_no_repository()
    {
        var github = new FakeGitHub
        {
            InstallationsJson = $$"""[{"id":10,"account":{"login":"acme","id":2,"type":"Organization"},"target_type":"Organization","repository_selection":"all","suspended_at":"2026-11-01T00:00:00Z","permissions":{{FakeGitHub.Permissions}}}]""",
        };

        var result = await GatewayOver(Connected(), github).FindRepositoryAsync("acme/web", "main", Token);

        Assert.Equal(GitHubRepositoryFailure.NotInstalled, result.Failure);
        Assert.DoesNotContain(github.Requests, r => r.Path.EndsWith("/access_tokens", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Only_the_installation_on_the_owners_account_is_asked_for_its_repositories()
    {
        var github = new FakeGitHub();

        await GatewayOver(Connected(), github).FindRepositoryAsync("acme/web", "main", Token);

        Assert.Equal(["/app/installations/10/access_tokens"], github.Requests.Where(r => r.Path.EndsWith("/access_tokens", StringComparison.Ordinal)).Select(r => r.Path));
    }

    [Fact]
    public async Task The_lookup_is_made_as_the_App_then_as_the_installation_and_only_reads()
    {
        var github = new FakeGitHub();

        await GatewayOver(Connected(), github).FindRepositoryAsync("acme/web", "main", Token);

        Assert.StartsWith("Bearer ", github.Requests.Single(r => r.Path == "/app/installations").Authorization, StringComparison.Ordinal);
        Assert.EndsWith(FakeGitHub.InstallationToken, github.Requests.Single(r => r.Path == "/repositories/1/branches/main").Authorization, StringComparison.Ordinal);
        Assert.All(github.Requests, r => Assert.Contains(r.Method.Method, new[] { "GET", "POST" }));
    }

    [Fact]
    public async Task Nothing_stored_means_not_connected_and_GitHub_is_not_asked()
    {
        var github = new FakeGitHub();

        Assert.Equal(GitHubRepositoryFailure.NotConnected, (await GatewayOver(new FakeStores(), github).FindRepositoryAsync("acme/web", "main", Token)).Failure);
        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task A_stored_key_that_is_not_a_key_is_unreadable_and_GitHub_is_not_asked()
    {
        var github = new FakeGitHub();
        var stores = new FakeStores { AppId = AppId, Secrets = { [KeyName] = "not a pem key at all" } };

        Assert.Equal(GitHubRepositoryFailure.KeyUnreadable, (await GatewayOver(stores, github).FindRepositoryAsync("acme/web", "main", Token)).Failure);
        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task A_credential_GitHub_refuses_is_rejected()
    {
        var result = await GatewayOver(Connected(), new FakeGitHub { Override = r => r.RequestUri!.AbsolutePath == "/app/installations" ? FakeGitHub.Json(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""") : null })
            .FindRepositoryAsync("acme/web", "main", Token);

        Assert.Equal(GitHubRepositoryFailure.Rejected, result.Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task GitHub_failing_to_serve_the_branch_is_unreachable(HttpStatusCode status)
    {
        var github = new FakeGitHub
        {
            Override = r => r.RequestUri!.AbsolutePath.Contains("/branches/", StringComparison.Ordinal) ? FakeGitHub.Json(status, """{"message":"x"}""") : null,
        };

        Assert.Equal(GitHubRepositoryFailure.Unreachable, (await GatewayOver(Connected(), github).FindRepositoryAsync("acme/web", "main", Token)).Failure);
    }

    [Fact]
    public async Task A_lookup_that_outlasts_its_deadline_is_unreachable()
    {
        var result = await GatewayOver(Connected(), new FakeGitHub { Hang = true }, TimeSpan.FromMilliseconds(200)).FindRepositoryAsync("acme/web", "main", Token);

        Assert.Equal(GitHubRepositoryFailure.Unreachable, result.Failure);
    }

    [Fact]
    public async Task A_lookup_cancelled_by_the_caller_is_cancelled_not_reported_as_unreachable()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GatewayOver(Connected(), new FakeGitHub()).FindRepositoryAsync("acme/web", "main", cancelled.Token));
    }

    [Fact]
    public async Task A_lookup_stores_nothing()
    {
        var stores = Connected();
        var before = stores.Secrets.ToDictionary(p => p.Key, p => p.Value);

        await GatewayOver(stores, new FakeGitHub()).FindRepositoryAsync("acme/web", "main", Token);

        Assert.Equal(before, stores.Secrets);
        Assert.Equal(AppId, stores.AppId);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
