using System.Net;
using System.Security.Cryptography;
using Hephaestus.Core.GitHub;

namespace Hephaestus.GitHub.Tests;

/// <summary>Story 2.4, AD-4, AD-5: the gateway reads the labels of an issue and changes them as a set, and tells a wait or an outage, which are tried again, from a refusal.</summary>
public class GitHubIssueLabelsTests
{
    private const long AppId = 123456;
    private const string KeyName = GitHubAppCredential.PrivateKeySecretName;
    private const string Key = "0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b:g1:label:0";

    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FakeStores Connected() => new() { AppId = AppId, Secrets = { [KeyName] = RSA.Create(2048).ExportRSAPrivateKeyPem() } };

    private static GitHubGateway GatewayOver(FakeStores stores, FakeGitHub github, TimeSpan? deadline = null, GitHubRateLimit? limit = null) =>
        new(stores, stores, stores, new FixedTime(Now), () => github, deadline, limit);

    private static FakeGitHub WithIssue(params string[] labels)
    {
        var github = new FakeGitHub();
        github.IssueLabels["1/7"] = [.. labels];
        return github;
    }

    [Fact]
    public async Task The_labels_the_issue_has_now_are_read_as_the_installation_that_holds_the_repository()
    {
        var github = WithIssue("hephaestus", "bug");

        var result = await GatewayOver(Connected(), github).ReadIssueLabelsAsync(1, 7, Token);

        Assert.Equal(GitHubIssueFailure.None, result.Failure);
        Assert.Equal(["hephaestus", "bug"], result.Labels);
        Assert.EndsWith(FakeGitHub.InstallationToken, github.Requests.Last().Authorization, StringComparison.Ordinal);
        Assert.Empty(github.LabelWrites);
    }

    [Fact]
    public async Task A_change_adds_the_new_label_before_it_removes_the_old_one_and_answers_with_the_labels_that_are_left()
    {
        var github = WithIssue("hephaestus", "hephaestus:running", "bug");

        var result = await GatewayOver(Connected(), github).ChangeIssueLabelsAsync(1, 7, ["hephaestus:queued"], ["hephaestus:running"], Key, Token);

        Assert.Equal(GitHubIssueFailure.None, result.Failure);
        Assert.Equal(["hephaestus", "bug", "hephaestus:queued"], result.Labels);
        Assert.Equal(["POST", "DELETE"], github.LabelWrites.Select(w => w.Method.Method));
        Assert.Equal("""["hephaestus:queued"]""", github.LabelWrites[0].Body);
        Assert.EndsWith("/repositories/1/issues/7/labels/hephaestus:running", github.LabelWrites[1].Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_change_that_only_removes_sends_no_add_and_one_that_only_adds_sends_no_remove()
    {
        var github = WithIssue("hephaestus:running");
        var gateway = GatewayOver(Connected(), github);

        await gateway.ChangeIssueLabelsAsync(1, 7, [], ["hephaestus:running"], Key, Token);
        await gateway.ChangeIssueLabelsAsync(1, 7, ["hephaestus:queued"], [], Key, Token);

        Assert.Equal(["DELETE", "POST"], github.LabelWrites.Select(w => w.Method.Method));
    }

    [Fact]
    public async Task A_label_that_is_gone_already_is_not_an_error_so_the_change_made_twice_is_one_change()
    {
        var github = WithIssue("hephaestus", "hephaestus:queued");
        var gateway = GatewayOver(Connected(), github);

        var first = await gateway.ChangeIssueLabelsAsync(1, 7, ["hephaestus:queued"], ["hephaestus:running"], Key, Token);
        var second = await gateway.ChangeIssueLabelsAsync(1, 7, ["hephaestus:queued"], ["hephaestus:running"], Key, Token);

        Assert.Equal(GitHubIssueFailure.None, first.Failure);
        Assert.Equal(first.Labels, second.Labels);
        Assert.Equal(["hephaestus", "hephaestus:queued"], second.Labels);
    }

    [Fact]
    public async Task An_issue_that_does_not_exist_is_refused_for_good_by_a_read_and_by_a_change()
    {
        var github = new FakeGitHub();
        var gateway = GatewayOver(Connected(), github);

        Assert.Equal(GitHubIssueFailure.IssueNotFound, (await gateway.ReadIssueLabelsAsync(1, 7, Token)).Failure);
        Assert.Equal(GitHubIssueFailure.IssueNotFound, (await gateway.ChangeIssueLabelsAsync(1, 7, ["hephaestus:queued"], [], Key, Token)).Failure);
        Assert.Equal(GitHubIssueFailure.IssueNotFound, (await gateway.ChangeIssueLabelsAsync(1, 7, [], ["hephaestus:running"], Key, Token)).Failure);
    }

    [Fact]
    public async Task An_issue_that_GitHub_says_is_gone_is_the_same_refusal()
    {
        var github = WithIssue("hephaestus");
        github.Override = r => r.RequestUri!.AbsolutePath.Contains("/labels", StringComparison.Ordinal) ? FakeGitHub.Json(HttpStatusCode.Gone, """{"message":"Gone"}""") : null;

        Assert.Equal(GitHubIssueFailure.IssueNotFound, (await GatewayOver(Connected(), github).ReadIssueLabelsAsync(1, 7, Token)).Failure);
    }

    [Fact]
    public async Task A_repository_the_App_is_not_installed_on_is_refused_for_good_and_nothing_is_written()
    {
        var github = WithIssue("hephaestus");
        github.Repositories[10] = [];
        github.Repositories[11] = [];

        var result = await GatewayOver(Connected(), github).ChangeIssueLabelsAsync(1, 7, ["hephaestus:queued"], [], Key, Token);

        Assert.Equal(GitHubIssueFailure.NotInstalled, result.Failure);
        Assert.Empty(github.LabelWrites);
    }

    [Fact]
    public async Task A_suspended_installation_reaches_nothing()
    {
        var github = WithIssue("hephaestus");
        github.InstallationsJson = $$"""
            [{"id":10,"account":{"login":"acme","id":2,"type":"Organization"},"target_type":"Organization","repository_selection":"all","suspended_at":"2026-01-01T00:00:00Z","permissions":{{FakeGitHub.Permissions}}}]
            """;

        Assert.Equal(GitHubIssueFailure.NotInstalled, (await GatewayOver(Connected(), github).ReadIssueLabelsAsync(1, 7, Token)).Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_refusal_of_what_was_asked_for_example_a_permission_that_was_withdrawn_is_for_good(HttpStatusCode status)
    {
        var github = WithIssue("hephaestus");
        github.Override = r => r.Method != HttpMethod.Get && r.RequestUri!.AbsolutePath.Contains("/labels", StringComparison.Ordinal)
            ? FakeGitHub.Json(status, """{"message":"Resource not accessible by integration"}""")
            : null;

        var result = await GatewayOver(Connected(), github).ChangeIssueLabelsAsync(1, 7, ["hephaestus:queued"], [], Key, Token);

        Assert.Equal(GitHubIssueFailure.Rejected, result.Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task GitHub_failing_to_serve_is_unreachable_and_is_tried_again(HttpStatusCode status)
    {
        var github = WithIssue("hephaestus");
        github.Override = r => r.RequestUri!.AbsolutePath.Contains("/labels", StringComparison.Ordinal) ? FakeGitHub.Json(status, """{"message":"x"}""") : null;

        Assert.Equal(GitHubIssueFailure.Unreachable, (await GatewayOver(Connected(), github).ReadIssueLabelsAsync(1, 7, Token)).Failure);
    }

    [Fact]
    public async Task A_connection_that_fails_is_unreachable()
    {
        var github = WithIssue("hephaestus");
        github.Override = _ => throw new HttpRequestException("No route to host.");

        Assert.Equal(GitHubIssueFailure.Unreachable, (await GatewayOver(Connected(), github).ReadIssueLabelsAsync(1, 7, Token)).Failure);
    }

    [Fact]
    public async Task A_request_that_outlasts_its_deadline_is_unreachable()
    {
        var result = await GatewayOver(Connected(), new FakeGitHub { Hang = true }, TimeSpan.FromMilliseconds(200)).ReadIssueLabelsAsync(1, 7, Token);

        Assert.Equal(GitHubIssueFailure.Unreachable, result.Failure);
    }

    [Fact]
    public async Task A_rate_limit_is_a_wait_until_the_time_GitHub_states_and_the_gate_stays_closed_for_the_next_request()
    {
        var github = WithIssue("hephaestus");
        github.Override = r =>
        {
            if (!r.RequestUri!.AbsolutePath.Contains("/labels", StringComparison.Ordinal))
            {
                return null;
            }

            var response = FakeGitHub.Json(HttpStatusCode.TooManyRequests, """{"message":"Rate limit"}""");
            response.Headers.TryAddWithoutValidation("Retry-After", "120");
            return response;
        };
        var limit = new GitHubRateLimit(new FixedTime(Now));
        var gateway = GatewayOver(Connected(), github, limit: limit);

        var first = await gateway.ReadIssueLabelsAsync(1, 7, Token);
        var requests = github.Requests.Count;
        var second = await gateway.ChangeIssueLabelsAsync(1, 7, ["hephaestus:queued"], [], Key, Token);

        Assert.Equal((GitHubIssueFailure.RateLimited, Now.AddSeconds(120)), (first.Failure, first.WaitUntil));
        Assert.Equal((GitHubIssueFailure.RateLimited, Now.AddSeconds(120)), (second.Failure, second.WaitUntil));
        Assert.Equal(requests, github.Requests.Count);
    }

    [Fact]
    public async Task Without_a_connection_to_GitHub_nothing_is_asked()
    {
        var github = WithIssue("hephaestus");

        var result = await GatewayOver(new FakeStores(), github).ReadIssueLabelsAsync(1, 7, Token);

        Assert.Equal(GitHubIssueFailure.NotConnected, result.Failure);
        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task A_stored_key_that_is_no_key_is_unreadable()
    {
        var stores = new FakeStores { AppId = AppId, Secrets = { [KeyName] = "not a key" } };

        Assert.Equal(GitHubIssueFailure.KeyUnreadable, (await GatewayOver(stores, WithIssue("hephaestus")).ReadIssueLabelsAsync(1, 7, Token)).Failure);
    }

    [Fact]
    public async Task A_change_without_the_key_of_its_effect_is_not_made()
    {
        var github = WithIssue("hephaestus");

        await Assert.ThrowsAsync<ArgumentException>(() => GatewayOver(Connected(), github).ChangeIssueLabelsAsync(1, 7, ["hephaestus:queued"], [], " ", Token));

        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task A_change_cancelled_by_the_caller_is_cancelled_not_reported_as_unreachable()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GatewayOver(Connected(), WithIssue("hephaestus")).ChangeIssueLabelsAsync(1, 7, ["hephaestus:queued"], [], Key, cancelled.Token));
    }

    [Fact]
    public async Task A_refusal_that_arrives_while_the_host_is_stopping_is_not_taken_for_GitHub_refusing()
    {
        using var stopping = new CancellationTokenSource();
        var github = WithIssue("hephaestus");
        github.Override = r =>
        {
            if (!r.RequestUri!.AbsolutePath.Contains("/labels", StringComparison.Ordinal))
            {
                return null;
            }

            stopping.Cancel();
            return FakeGitHub.Json(HttpStatusCode.Forbidden, """{"message":"Resource not accessible by integration"}""");
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GatewayOver(Connected(), github).ReadIssueLabelsAsync(1, 7, stopping.Token));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
