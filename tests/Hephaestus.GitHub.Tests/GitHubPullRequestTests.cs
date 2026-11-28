using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Secrets;

namespace Hephaestus.GitHub.Tests;

/// <summary>
/// Story 3.13, AD-4, AD-5, FR-4, FR-21, FR-36: the gateway opens the pull request of a Task branch under the installation's identity, finds the one a branch has by its head branch and updates it instead of opening a second, masks stored secret
/// values in what it sends, and tells a wait or an outage, which are tried again, from a refusal, which is for good.
/// </summary>
public class GitHubPullRequestTests
{
    private const long AppId = 123456;
    private const string KeyName = GitHubAppCredential.PrivateKeySecretName;
    private const string Head = "hephaestus/7-add-a-login-page";
    private const string Key = "task:s1:pull-request:0";

    private static readonly DateTimeOffset Now = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FakeStores Connected() => new() { AppId = AppId, AppSlug = "hephaestus-test", Secrets = { [KeyName] = RSA.Create(2048).ExportRSAPrivateKeyPem() } };

    private static GitHubGateway GatewayOver(FakeStores stores, FakeGitHub github, GitHubRateLimit? limit = null) =>
        new(stores, stores, stores, new FixedTime(Now), () => github, rateLimit: limit);

    private static PullRequestPublication Request(string title = "Add a login page", string body = "## Task\n\nIssue #7\n") => new(1, Head, "main", title, body);

    [Fact]
    public async Task A_branch_with_no_pull_request_has_one_opened_from_the_Task_branch_to_the_Base_Branch_with_the_title_and_the_description()
    {
        var github = new FakeGitHub();

        var result = await GatewayOver(Connected(), github).PublishAsync(Request(), Key, Token);

        Assert.Equal((PullRequestOutcome.Opened, GitHubIssueFailure.None, 100), (result.Outcome, result.Failure, result.Number));
        var created = Assert.Single(github.PullRequests, w => w.Method == HttpMethod.Post);
        Assert.EndsWith("/repositories/1/pulls", created.Path, StringComparison.Ordinal);
        var document = JsonDocument.Parse(created.Body).RootElement;
        Assert.Equal((Head, "main", "Add a login page", "## Task\n\nIssue #7\n"), (document.GetProperty("head").GetString(), document.GetProperty("base").GetString(), document.GetProperty("title").GetString(), document.GetProperty("body").GetString()));
        Assert.False(document.TryGetProperty("draft", out var draft) && draft.GetBoolean());
        Assert.EndsWith(FakeGitHub.InstallationToken, github.Requests.Last(r => r.Method == HttpMethod.Post && r.Path.EndsWith("/pulls", StringComparison.Ordinal)).Authorization, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_branch_is_looked_at_first_by_its_head_qualified_by_the_owner()
    {
        var github = new FakeGitHub();

        await GatewayOver(Connected(), github).PublishAsync(Request(), Key, Token);

        var search = github.PullRequests.First();
        Assert.Equal(HttpMethod.Get, search.Method);
        Assert.Contains("head=acme%3Ahephaestus%2F7-add-a-login-page", search.Query, StringComparison.Ordinal);
        Assert.Contains("state=open", search.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_branch_that_has_a_pull_request_already_has_it_updated_and_no_second_is_opened()
    {
        var github = new FakeGitHub();
        github.Pulls[1] = [(12, Head, "Earlier title", "Earlier text")];

        var result = await GatewayOver(Connected(), github).PublishAsync(Request(), Key, Token);

        Assert.Equal((PullRequestOutcome.Updated, 12), (result.Outcome, result.Number));
        Assert.DoesNotContain(github.PullRequests, w => w.Method == HttpMethod.Post);
        Assert.Equal((12, Head, "Add a login page", "## Task\n\nIssue #7\n"), Assert.Single(github.Pulls[1]));
        Assert.EndsWith("/repositories/1/pulls/12", Assert.Single(github.PullRequests, w => w.Method == HttpMethod.Patch).Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publishing_twice_is_one_pull_request()
    {
        var github = new FakeGitHub();
        var gateway = GatewayOver(Connected(), github);

        var first = await gateway.PublishAsync(Request(), Key, Token);
        var second = await gateway.PublishAsync(Request(body: "Later."), Key, Token);

        Assert.Equal((PullRequestOutcome.Opened, PullRequestOutcome.Updated, first.Number), (first.Outcome, second.Outcome, second.Number));
        Assert.Single(github.Pulls[1]);
    }

    [Fact]
    public async Task A_pull_request_of_another_branch_is_not_taken_for_the_one_of_the_Task()
    {
        var github = new FakeGitHub();
        github.Pulls[1] = [(12, "hephaestus/8-other", "Other", "Other.")];
        github.Override = r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/pulls", StringComparison.Ordinal)
            ? FakeGitHub.Json(HttpStatusCode.OK, """[{"number":12,"state":"open","head":{"ref":"hephaestus/8-other","label":"acme:hephaestus/8-other","sha":"a"},"base":{"ref":"main","label":"acme:main","sha":"b"}}]""")
            : null;

        var result = await GatewayOver(Connected(), github).PublishAsync(Request(), Key, Token);

        Assert.Equal(PullRequestOutcome.Opened, result.Outcome);
    }

    [Fact]
    public async Task A_stored_secret_value_in_the_title_or_the_description_is_masked_before_the_request_leaves_even_one_stored_a_moment_ago()
    {
        var stores = Connected();
        var github = new FakeGitHub();
        var gateway = GatewayOver(stores, github);
        stores.Secrets["deploy-token"] = "s3cr3t-value-0123456789";

        await gateway.PublishAsync(Request(title: "Use s3cr3t-value-0123456789", body: "It printed s3cr3t-value-0123456789."), Key, Token);

        var document = JsonDocument.Parse(Assert.Single(github.PullRequests, w => w.Method == HttpMethod.Post).Body).RootElement;
        Assert.Equal($"Use {SecretMasker.Replacement}", document.GetProperty("title").GetString());
        Assert.Equal($"It printed {SecretMasker.Replacement}.", document.GetProperty("body").GetString());
    }

    [Fact]
    public async Task A_secret_in_an_update_is_masked_too()
    {
        var stores = Connected();
        var github = new FakeGitHub();
        github.Pulls[1] = [(12, Head, "t", "b")];
        stores.Secrets["deploy-token"] = "s3cr3t-value-0123456789";

        await GatewayOver(stores, github).PublishAsync(Request(body: "Leaked s3cr3t-value-0123456789"), Key, Token);

        Assert.Equal($"Leaked {SecretMasker.Replacement}", github.Pulls[1][0].Body);
    }

    [Fact]
    public async Task A_repository_that_does_not_exist_or_holds_no_installation_is_refused_for_good()
    {
        var github = new FakeGitHub();

        Assert.Equal(GitHubIssueFailure.NotInstalled, (await GatewayOver(Connected(), github).PublishAsync(new PullRequestPublication(99, Head, "main", "t", "b"), Key, Token)).Failure);
        github.Override = r => r.RequestUri!.AbsolutePath == "/repositories/1" ? FakeGitHub.Json(HttpStatusCode.NotFound, """{"message":"Not Found"}""") : null;
        Assert.Equal(GitHubIssueFailure.IssueNotFound, (await GatewayOver(Connected(), github).PublishAsync(Request(), Key, Token)).Failure);
    }

    [Fact]
    public async Task A_pull_request_GitHub_refuses_is_refused_for_good_and_a_server_error_is_an_outage()
    {
        var unprocessable = new FakeGitHub { Override = r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/pulls", StringComparison.Ordinal) ? FakeGitHub.Json(HttpStatusCode.UnprocessableEntity, """{"message":"No commits between main and the branch"}""") : null };
        var failing = new FakeGitHub { Override = r => r.RequestUri!.AbsolutePath.EndsWith("/pulls", StringComparison.Ordinal) ? FakeGitHub.Json(HttpStatusCode.BadGateway, """{"message":"x"}""") : null };

        Assert.Equal(GitHubIssueFailure.Rejected, (await GatewayOver(Connected(), unprocessable).PublishAsync(Request(), Key, Token)).Failure);
        Assert.Equal(GitHubIssueFailure.Unreachable, (await GatewayOver(Connected(), failing).PublishAsync(Request(), Key, Token)).Failure);
    }

    [Fact]
    public async Task A_rate_limit_is_a_wait_until_the_time_GitHub_states_and_the_gate_stays_closed()
    {
        var github = new FakeGitHub();
        github.Override = r =>
        {
            if (!r.RequestUri!.AbsolutePath.EndsWith("/pulls", StringComparison.Ordinal))
            {
                return null;
            }

            var response = FakeGitHub.Json(HttpStatusCode.TooManyRequests, """{"message":"Rate limit"}""");
            response.Headers.TryAddWithoutValidation("Retry-After", "120");
            return response;
        };
        var gateway = GatewayOver(Connected(), github, new GitHubRateLimit(new FixedTime(Now)));

        var first = await gateway.PublishAsync(Request(), Key, Token);
        var requests = github.Requests.Count;
        var second = await gateway.PublishAsync(Request(), Key, Token);

        Assert.Equal((PullRequestOutcome.Failed, GitHubIssueFailure.RateLimited, Now.AddSeconds(120)), (first.Outcome, first.Failure, first.WaitUntil));
        Assert.Equal((GitHubIssueFailure.RateLimited, Now.AddSeconds(120)), (second.Failure, second.WaitUntil));
        Assert.Equal(requests, github.Requests.Count);
    }

    [Fact]
    public async Task Without_a_connection_to_GitHub_nothing_is_published()
    {
        var github = new FakeGitHub();

        var result = await GatewayOver(new FakeStores(), github).PublishAsync(Request(), Key, Token);

        Assert.Equal(GitHubIssueFailure.NotConnected, result.Failure);
        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task A_publication_without_the_key_of_its_effect_is_not_made()
    {
        var github = new FakeGitHub();

        await Assert.ThrowsAsync<ArgumentException>(() => GatewayOver(Connected(), github).PublishAsync(Request(), " ", Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => GatewayOver(Connected(), github).PublishAsync(null!, Key, Token));

        Assert.Empty(github.Requests);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("hephaestus/")]
    [InlineData("feature/7-x")]
    public void A_pull_request_is_opened_from_nothing_but_a_Task_branch(string head)
    {
        Assert.Throws<ArgumentException>(() => new PullRequestPublication(1, head, "main", "t", "b"));
        Assert.Throws<ArgumentException>(() => new PullRequestPublication(1, Head, " ", "t", "b"));
        Assert.Throws<ArgumentException>(() => new PullRequestPublication(1, Head, "main", " ", "b"));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
