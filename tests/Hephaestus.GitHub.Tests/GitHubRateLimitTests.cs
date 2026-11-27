using System.Net;
using System.Security.Cryptography;
using Hephaestus.Core.GitHub;

namespace Hephaestus.GitHub.Tests;

/// <summary>NFR-12, Story 2.1: a rate-limit answer closes one gate until the time GitHub states, for the gateway as for the poller; the wait is temporary unavailability and loses nothing.</summary>
public class GitHubRateLimitTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FakeStores Connected() => new() { AppId = 123456, Secrets = { [GitHubAppCredential.PrivateKeySecretName] = RSA.Create(2048).ExportRSAPrivateKeyPem() } };

    private static HttpResponseMessage Limited(string? retryAfter = "120") => PollerFixture.RateLimited(HttpStatusCode.TooManyRequests, retryAfter);

    [Fact]
    public void A_gate_is_open_until_it_is_closed_and_open_again_when_the_time_has_come()
    {
        var time = new MovingTime(Noon);
        var gate = new GitHubRateLimit(time);
        Assert.Null(gate.ClosedUntil);

        gate.CloseUntil(Noon.AddMinutes(5));
        Assert.Equal(Noon.AddMinutes(5), gate.ClosedUntil);

        time.Now = Noon.AddMinutes(5);
        Assert.Null(gate.ClosedUntil);
    }

    [Fact]
    public void A_later_answer_with_an_earlier_time_does_not_shorten_a_wait_that_is_running()
    {
        var gate = new GitHubRateLimit(new MovingTime(Noon));
        gate.CloseUntil(Noon.AddMinutes(30));

        gate.CloseUntil(Noon.AddMinutes(1));

        Assert.Equal(Noon.AddMinutes(30), gate.ClosedUntil);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public void An_answer_that_is_not_a_429_or_a_403_is_no_rate_limit(HttpStatusCode status)
    {
        Assert.Null(GitHubRateLimit.WaitOf(status, _ => "30", "rate limit", Noon));
    }

    [Fact]
    public void A_403_with_no_sign_of_a_limit_is_a_refusal_not_a_rate_limit()
    {
        Assert.Null(GitHubRateLimit.WaitOf(HttpStatusCode.Forbidden, _ => null, "Resource not accessible by integration", Noon));
        Assert.Null(GitHubRateLimit.WaitOf(HttpStatusCode.Forbidden, name => name == "X-RateLimit-Remaining" ? "12" : null, null, Noon));
    }

    [Fact]
    public void A_retry_after_names_the_time_and_beats_every_other_sign()
    {
        var wait = GitHubRateLimit.WaitOf(HttpStatusCode.Forbidden, name => name == "Retry-After" ? "45" : "0", "rate limit", Noon);

        Assert.Equal(Noon.AddSeconds(45), wait);
    }

    [Fact]
    public void A_used_up_primary_limit_waits_for_the_reset_second_and_one_more()
    {
        var reset = Noon.AddMinutes(20).ToUnixTimeSeconds();

        var wait = GitHubRateLimit.WaitOf(HttpStatusCode.Forbidden, name => name switch { "X-RateLimit-Remaining" => "0", "X-RateLimit-Reset" => reset.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => null }, null, Noon);

        Assert.Equal(Noon.AddMinutes(20).AddSeconds(1), wait);
    }

    [Fact]
    public void A_reset_time_that_is_far_in_the_future_is_cut_to_the_longest_wait_and_a_reset_too_large_to_be_a_time_does_not_throw()
    {
        var far = GitHubRateLimit.WaitOf(HttpStatusCode.Forbidden, name => name switch { "X-RateLimit-Remaining" => "0", "X-RateLimit-Reset" => "9000000000000000000", _ => null }, null, Noon);
        var past = GitHubRateLimit.WaitOf(HttpStatusCode.Forbidden, name => name switch { "X-RateLimit-Remaining" => "0", "X-RateLimit-Reset" => "1", _ => null }, null, Noon);

        Assert.Equal(Noon.AddHours(2).AddSeconds(1), far);
        Assert.True(past < Noon);
    }

    [Fact]
    public void A_used_up_limit_with_no_reset_and_a_message_with_no_time_wait_a_minute()
    {
        Assert.Equal(Noon.AddMinutes(1), GitHubRateLimit.WaitOf(HttpStatusCode.Forbidden, name => name == "X-RateLimit-Remaining" ? "0" : null, null, Noon));
        Assert.Equal(Noon.AddMinutes(1), GitHubRateLimit.WaitOf(HttpStatusCode.Forbidden, _ => null, "You have exceeded a secondary RATE LIMIT.", Noon));
        Assert.Equal(Noon.AddMinutes(1), GitHubRateLimit.WaitOf(HttpStatusCode.TooManyRequests, _ => null, null, Noon));
    }

    [Fact]
    public void A_header_that_is_not_a_number_is_not_believed()
    {
        Assert.Equal(Noon.AddMinutes(1), GitHubRateLimit.WaitOf(HttpStatusCode.TooManyRequests, name => name == "Retry-After" ? "soon" : null, null, Noon));
        Assert.Equal(Noon.AddMinutes(1), GitHubRateLimit.WaitOf(HttpStatusCode.Forbidden, name => name switch { "X-RateLimit-Remaining" => "0", "X-RateLimit-Reset" => "later", _ => null }, null, Noon));
    }

    [Fact]
    public void An_exception_that_is_not_an_Octokit_answer_is_no_rate_limit()
    {
        Assert.Null(GitHubRateLimit.WaitOf(new HttpRequestException("no route"), Noon));
        Assert.Null(GitHubRateLimit.WaitOf(new Octokit.NotFoundException("gone", HttpStatusCode.NotFound), Noon));
    }

    [Fact]
    public async Task A_rate_limit_on_one_gateway_call_holds_back_the_next_and_it_goes_through_when_the_time_has_come()
    {
        var time = new MovingTime(Noon);
        var gate = new GitHubRateLimit(time);
        var stores = Connected();
        var github = new FakeGitHub { Override = request => request.RequestUri!.AbsolutePath == "/app" ? Limited() : null };
        var gateway = new GitHubGateway(stores, stores, stores, time, () => github, rateLimit: gate);

        var first = await gateway.CheckIdentityAsync(Token);

        Assert.Equal(GitHubIdentityFailure.Unreachable, first.Failure);
        Assert.Equal(Noon.AddSeconds(120), gate.ClosedUntil);

        github.Override = null;
        github.Requests.Clear();
        var second = await gateway.CheckIdentityAsync(Token);
        Assert.Equal(GitHubIdentityFailure.Unreachable, second.Failure);
        Assert.Empty(github.Requests);

        time.Now = Noon.AddSeconds(121);
        var third = await gateway.CheckIdentityAsync(Token);
        Assert.Equal(GitHubIdentityFailure.None, third.Failure);
    }

    [Fact]
    public async Task Every_gateway_read_waits_while_the_gate_is_closed_and_sends_nothing()
    {
        var time = new MovingTime(Noon);
        var gate = new GitHubRateLimit(time);
        gate.CloseUntil(Noon.AddMinutes(10));
        var stores = Connected();
        var github = new FakeGitHub();
        var gateway = new GitHubGateway(stores, stores, stores, time, () => github, rateLimit: gate);

        var identity = await gateway.CheckIdentityAsync(Token);
        var user = await gateway.FindUserAsync("alice", Token);
        var repository = await gateway.FindRepositoryAsync("acme/web", "main", Token);
        var file = await gateway.ReadFileAsync(1, "main", "hephaestus.yml", 1000, Token);
        var connect = await gateway.ConnectIdentityAsync(123456, RSA.Create(2048).ExportRSAPrivateKeyPem(), Token);

        Assert.Equal(GitHubIdentityFailure.Unreachable, identity.Failure);
        Assert.Equal(Hephaestus.Core.Trust.GitHubUserFailure.Unreachable, user.Failure);
        Assert.Equal(Hephaestus.Core.Projects.GitHubRepositoryFailure.Unreachable, repository.Failure);
        Assert.Equal(Hephaestus.Core.Projects.GitHubFileFailure.Unreachable, file.Failure);
        Assert.Equal(GitHubIdentityFailure.Unreachable, connect.Failure);
        Assert.Empty(github.Requests);
        Assert.Equal(123456, stores.AppId);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("repository")]
    [InlineData("file")]
    public async Task A_rate_limit_met_by_any_gateway_read_closes_the_gate_for_the_others(string read)
    {
        var time = new MovingTime(Noon);
        var gate = new GitHubRateLimit(time);
        var stores = Connected();
        var github = new FakeGitHub { Override = request => request.RequestUri!.AbsolutePath == "/app/installations" ? Limited("200") : null };
        var gateway = new GitHubGateway(stores, stores, stores, time, () => github, rateLimit: gate);

        _ = read switch
        {
            "user" => (object)await gateway.FindUserAsync("alice", Token),
            "repository" => await gateway.FindRepositoryAsync("adamcharusta/Hephaestus", "main", Token),
            _ => await gateway.ReadFileAsync(1, "main", "hephaestus.yml", 1000, Token),
        };

        Assert.Equal(Noon.AddSeconds(200), gate.ClosedUntil);
    }

    [Fact]
    public async Task A_gateway_with_no_gate_given_keeps_its_own()
    {
        var stores = Connected();
        var github = new FakeGitHub { Override = request => request.RequestUri!.AbsolutePath == "/app" ? Limited() : null };
        var gateway = new GitHubGateway(stores, stores, stores, new MovingTime(Noon), () => github);

        await gateway.CheckIdentityAsync(Token);
        github.Override = null;
        github.Requests.Clear();
        await gateway.CheckIdentityAsync(Token);

        Assert.Empty(github.Requests);
    }

    [Theory]
    [InlineData("""{"message":"You have exceeded a secondary rate limit. Please wait a few minutes before you try again."}""", "40", 40)]
    [InlineData("""{"message":"You have exceeded a secondary rate limit. Please wait a few minutes before you try again."}""", null, 60)]
    public async Task A_secondary_limit_Octokit_raises_waits_the_time_it_states_or_a_minute(string body, string? retryAfter, int seconds)
    {
        var time = new MovingTime(Noon);
        var gate = new GitHubRateLimit(time);
        var stores = Connected();
        var github = new FakeGitHub
        {
            Override = request =>
            {
                if (request.RequestUri!.AbsolutePath != "/app")
                {
                    return null;
                }

                var response = FakeGitHub.Json(HttpStatusCode.Forbidden, body);
                if (retryAfter is not null)
                {
                    response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
                }

                return response;
            },
        };

        await new GitHubGateway(stores, stores, stores, time, () => github, rateLimit: gate).CheckIdentityAsync(Token);

        Assert.Equal(Noon.AddSeconds(seconds), gate.ClosedUntil);
    }

    [Fact]
    public async Task A_primary_limit_Octokit_raises_waits_for_the_reset_time()
    {
        var time = new MovingTime(Noon);
        var gate = new GitHubRateLimit(time);
        var stores = Connected();
        var reset = Noon.AddMinutes(17).ToUnixTimeSeconds();
        var github = new FakeGitHub { Override = request => request.RequestUri!.AbsolutePath == "/app" ? PollerFixture.RateLimited(HttpStatusCode.Forbidden, remaining: "0", reset: reset) : null };

        await new GitHubGateway(stores, stores, stores, time, () => github, rateLimit: gate).CheckIdentityAsync(Token);

        Assert.Equal(Noon.AddMinutes(17).AddSeconds(1), gate.ClosedUntil);
    }

    [Fact]
    public async Task The_poller_and_the_gateway_share_one_gate()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.Override = request => request.RequestUri!.AbsolutePath.EndsWith("/issues/events", StringComparison.Ordinal) ? Limited() : null;
        var gateway = new GitHubGateway(fixture.Stores, fixture.Stores, fixture.Stores, fixture.Time, () => fixture.GitHub, rateLimit: fixture.Limit);

        await fixture.PollAsync();
        fixture.GitHub.Requests.Clear();
        var identity = await gateway.CheckIdentityAsync(Token);

        Assert.Equal(GitHubIdentityFailure.Unreachable, identity.Failure);
        Assert.Empty(fixture.GitHub.Requests);
    }
}
