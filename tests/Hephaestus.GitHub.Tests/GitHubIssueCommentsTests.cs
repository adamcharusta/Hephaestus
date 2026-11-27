using System.Net;
using System.Security.Cryptography;
using Hephaestus.Core.Effects;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Secrets;

namespace Hephaestus.GitHub.Tests;

/// <summary>
/// Story 2.5, AD-5, AD-19, FR-4: the gateway finds a comment by its hidden marker, only when Hephaestus's own account wrote it, and posts a
/// comment masked and ending in the marker; a wait, an outage, and a refusal are told apart as they are for labels.
/// </summary>
public class GitHubIssueCommentsTests
{
    private const long AppId = 123456;
    private const string KeyName = GitHubAppCredential.PrivateKeySecretName;
    private const string Key = "project:1:g1:comment:0";
    private const string Own = "hephaestus-test[bot]";
    private const string Marker = "<!-- hephaestus:effect=" + Key + " -->";

    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FakeStores Connected() => new() { AppId = AppId, AppSlug = "hephaestus-test", Secrets = { [KeyName] = RSA.Create(2048).ExportRSAPrivateKeyPem() } };

    private static GitHubGateway GatewayOver(FakeStores stores, FakeGitHub github, GitHubRateLimit? limit = null, SecretMasker? masker = null) =>
        new(stores, stores, stores, new FixedTime(Now), () => github, rateLimit: limit, masker: masker);

    private static FakeGitHub WithIssue(params (string Login, string Body)[] comments)
    {
        var github = new FakeGitHub();
        github.IssueComments["1/7"] = [.. comments.Select((c, index) => ((long)index + 1, c.Login, c.Body))];
        return github;
    }

    [Fact]
    public async Task A_comment_that_Hephaestus_wrote_and_that_ends_with_the_marker_of_the_key_is_found_as_the_installation_that_holds_the_repository()
    {
        var github = WithIssue(("alice", "Looks good."), (Own, $"**Ignored.**\n\n{Marker}"));

        var result = await GatewayOver(Connected(), github).FindIssueCommentAsync(1, 7, Key, Token);

        Assert.Equal((GitHubIssueFailure.None, 2L), (result.Failure, result.CommentId));
        Assert.EndsWith(FakeGitHub.InstallationToken, github.Requests.Last().Authorization, StringComparison.Ordinal);
        Assert.Empty(github.CommentWrites);
    }

    [Fact]
    public async Task The_account_is_told_by_its_login_whatever_the_case_and_a_body_that_ends_in_a_line_break_is_still_a_match()
    {
        var github = WithIssue(("Hephaestus-Test[bot]", $"**Ignored.**\r\n\r\n{Marker}\r\n"));

        Assert.Equal(1L, (await GatewayOver(Connected(), github).FindIssueCommentAsync(1, 7, Key, Token)).CommentId);
    }

    [Fact]
    public async Task A_marker_written_by_anyone_else_is_disregarded_and_so_is_one_that_is_not_the_last_line_or_is_of_another_key()
    {
        var github = WithIssue(
            ("mallory", $"Forged.\n\n{Marker}"),
            (Own, $"{Marker}\nand then some text"),
            (Own, "<!-- hephaestus:effect=project:1:g1:comment:1 -->"),
            (Own, "No marker at all."));

        var result = await GatewayOver(Connected(), github).FindIssueCommentAsync(1, 7, Key, Token);

        Assert.Equal((GitHubIssueFailure.None, (long?)null), (result.Failure, result.CommentId));
    }

    [Fact]
    public async Task An_App_connected_before_its_slug_was_kept_has_it_read_and_stored_and_the_own_account_is_then_told()
    {
        var stores = Connected();
        stores.AppSlug = null;
        var github = WithIssue((Own, Marker));

        var result = await GatewayOver(stores, github).FindIssueCommentAsync(1, 7, Key, Token);

        Assert.Equal((GitHubIssueFailure.None, 1L), (result.Failure, result.CommentId));
        Assert.Equal("hephaestus-test", stores.AppSlug);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, GitHubIssueFailure.Rejected)]
    [InlineData(HttpStatusCode.BadGateway, GitHubIssueFailure.Unreachable)]
    public async Task When_the_slug_is_not_known_and_cannot_be_read_nothing_is_answered_and_the_failure_is_the_one_GitHub_gave(HttpStatusCode status, GitHubIssueFailure expected)
    {
        var stores = Connected();
        stores.AppSlug = null;
        var github = WithIssue((Own, Marker));
        github.AppStatus = status;

        var result = await GatewayOver(stores, github).FindIssueCommentAsync(1, 7, Key, Token);

        Assert.Equal((expected, (long?)null), (result.Failure, result.CommentId));
        Assert.Empty(github.CommentWrites);
        Assert.DoesNotContain(github.Requests, r => r.Path.Contains("/comments", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_a_connection_or_with_a_key_that_is_no_key_the_own_account_cannot_be_told_and_nothing_is_asked_about_comments()
    {
        var unconnected = new FakeStores();
        var unreadable = new FakeStores { AppId = AppId, Secrets = { [KeyName] = "not a key" } };
        var github = WithIssue((Own, Marker));

        Assert.Equal(GitHubIssueFailure.NotConnected, (await GatewayOver(unconnected, github).FindIssueCommentAsync(1, 7, Key, Token)).Failure);
        Assert.Equal(GitHubIssueFailure.KeyUnreadable, (await GatewayOver(unreadable, github).FindIssueCommentAsync(1, 7, Key, Token)).Failure);
        Assert.DoesNotContain(github.Requests, r => r.Path.Contains("/comments", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_posted_comment_is_the_text_and_then_the_hidden_marker_as_the_last_line()
    {
        var github = WithIssue();

        var result = await GatewayOver(Connected(), github).PostIssueCommentAsync(1, 7, "**Ignored.** `bob` is not a Trusted User, so the `hephaestus` label has no effect.  \n", Key, Token);

        Assert.Equal((GitHubIssueFailure.None, 1L), (result.Failure, result.CommentId));
        var write = Assert.Single(github.CommentWrites);
        Assert.EndsWith("/repositories/1/issues/7/comments", write.Path, StringComparison.Ordinal);
        Assert.Equal($"**Ignored.** `bob` is not a Trusted User, so the `hephaestus` label has no effect.\n\n{Marker}", write.Body);
        Assert.Equal(Marker, write.Body.Split('\n')[^1]);
    }

    [Fact]
    public async Task A_stored_secret_value_in_the_body_is_masked_before_the_request_leaves_even_one_stored_a_moment_ago()
    {
        var stores = Connected();
        var github = WithIssue();
        var gateway = GatewayOver(stores, github);
        stores.Secrets["deploy-token"] = "s3cr3t-value-0123456789";

        await gateway.PostIssueCommentAsync(1, 7, "Failed with s3cr3t-value-0123456789 in the log.", Key, Token);

        var body = Assert.Single(github.CommentWrites).Body;
        Assert.DoesNotContain("s3cr3t-value-0123456789", body, StringComparison.Ordinal);
        Assert.StartsWith($"Failed with {SecretMasker.Replacement} in the log.", body, StringComparison.Ordinal);
        Assert.EndsWith(Marker, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_masker_the_host_gives_is_the_one_that_masks_not_one_the_gateway_builds_for_itself()
    {
        var hosts = new FakeStores { Secrets = { ["deploy-token"] = "another-secret-9876543210" } };
        var github = WithIssue();

        await GatewayOver(Connected(), github, masker: new SecretMasker(hosts)).PostIssueCommentAsync(1, 7, "Value: another-secret-9876543210", Key, Token);

        Assert.StartsWith($"Value: {SecretMasker.Replacement}", Assert.Single(github.CommentWrites).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_issue_that_does_not_exist_is_refused_for_good_by_a_search_and_by_a_post()
    {
        var gateway = GatewayOver(Connected(), new FakeGitHub());

        Assert.Equal(GitHubIssueFailure.IssueNotFound, (await gateway.FindIssueCommentAsync(1, 7, Key, Token)).Failure);
        Assert.Equal(GitHubIssueFailure.IssueNotFound, (await gateway.PostIssueCommentAsync(1, 7, "Text.", Key, Token)).Failure);
    }

    [Fact]
    public async Task A_withdrawn_permission_is_a_refusal_for_good_and_a_server_error_is_an_outage()
    {
        var forbidden = WithIssue();
        forbidden.Override = r => r.Method == HttpMethod.Post ? FakeGitHub.Json(HttpStatusCode.Forbidden, """{"message":"Resource not accessible by integration"}""") : null;
        var failing = WithIssue();
        failing.Override = r => r.RequestUri!.AbsolutePath.EndsWith("/comments", StringComparison.Ordinal) ? FakeGitHub.Json(HttpStatusCode.BadGateway, """{"message":"x"}""") : null;

        Assert.Equal(GitHubIssueFailure.Rejected, (await GatewayOver(Connected(), forbidden).PostIssueCommentAsync(1, 7, "Text.", Key, Token)).Failure);
        Assert.Equal(GitHubIssueFailure.Unreachable, (await GatewayOver(Connected(), failing).FindIssueCommentAsync(1, 7, Key, Token)).Failure);
    }

    [Fact]
    public async Task A_rate_limit_is_a_wait_until_the_time_GitHub_states_and_the_gate_stays_closed()
    {
        var github = WithIssue();
        github.Override = r =>
        {
            if (!r.RequestUri!.AbsolutePath.EndsWith("/comments", StringComparison.Ordinal))
            {
                return null;
            }

            var response = FakeGitHub.Json(HttpStatusCode.TooManyRequests, """{"message":"Rate limit"}""");
            response.Headers.TryAddWithoutValidation("Retry-After", "120");
            return response;
        };
        var gateway = GatewayOver(Connected(), github, new GitHubRateLimit(new FixedTime(Now)));

        var first = await gateway.PostIssueCommentAsync(1, 7, "Text.", Key, Token);
        var requests = github.Requests.Count;
        var second = await gateway.FindIssueCommentAsync(1, 7, Key, Token);

        Assert.Equal((GitHubIssueFailure.RateLimited, Now.AddSeconds(120)), (first.Failure, first.WaitUntil));
        Assert.Equal((GitHubIssueFailure.RateLimited, Now.AddSeconds(120)), (second.Failure, second.WaitUntil));
        Assert.Equal(requests, github.Requests.Count);
    }

    [Fact]
    public async Task Without_a_connection_to_GitHub_nothing_is_posted()
    {
        var github = WithIssue();

        var result = await GatewayOver(new FakeStores(), github).PostIssueCommentAsync(1, 7, "Text.", Key, Token);

        Assert.Equal(GitHubIssueFailure.NotConnected, result.Failure);
        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task A_comment_or_a_search_without_the_key_of_its_effect_is_not_made()
    {
        var github = WithIssue();
        var gateway = GatewayOver(Connected(), github);

        await Assert.ThrowsAsync<ArgumentException>(() => gateway.PostIssueCommentAsync(1, 7, "Text.", " ", Token));
        await Assert.ThrowsAsync<ArgumentException>(() => gateway.FindIssueCommentAsync(1, 7, "", Token));

        Assert.Empty(github.Requests);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
