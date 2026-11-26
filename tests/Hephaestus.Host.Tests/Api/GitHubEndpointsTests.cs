using System.Net;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Host.Cli;

namespace Hephaestus.Host.Tests.Api;

/// <summary>Story 1.7 on the control API: the endpoints of the GitHub identity, the problems they answer, and what no response of theirs may contain.</summary>
public class GitHubEndpointsTests
{
    private const string Key = "-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEAu1SU1LfVLPHCozMxH2Mo4lgOEePzNm0tRgeLezV6ffAt0gun\n-----END RSA PRIVATE KEY-----";

    private static string Compact(string json) => System.Text.RegularExpressions.Regex.Replace(json, @"\s*\n\s*", string.Empty);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task<RunningHost> StartAsync(FakeCodeHost gateway, Language language = Language.English) =>
        RunningHost.StartAsync(language, configure: builder => builder.Services.AddSingleton<ICodeHost>(gateway));

    private static ControlApiClient ClientFor(RunningHost host, Language language = Language.English) => new(host.Settings.ControlSocketPath, language);

    [Fact]
    public async Task Put_hands_the_id_and_the_key_in_the_body_to_the_gateway_and_answers_the_identity()
    {
        var gateway = new FakeCodeHost();
        await using var host = await StartAsync(gateway);
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/github/app/123456", Key, Token);

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Equal([(123456L, Key)], gateway.Connects);
        Assert.Equal(
            Compact("""
                {"appId":123456,"slug":"hephaestus-test","name":"Hephaestus Test","owner":"adamcharusta","ready":false,
                "installations":[
                {"id":10,"account":"acme","accountType":"Organization","repositorySelection":"all","suspended":false,"repositories":["acme/api","acme/web"]},
                {"id":11,"account":"adamcharusta","accountType":"User","repositorySelection":"selected","suspended":false,"repositories":[]},
                {"id":12,"account":"old-org","accountType":"Organization","repositorySelection":"all","suspended":true,"repositories":[]}],
                "permissions":[
                {"name":"contents","required":"write","granted":"write","present":true},
                {"name":"issues","required":"write","granted":"write","present":true},
                {"name":"pull-requests","required":"write","granted":"read","present":false},
                {"name":"metadata","required":"read","granted":"read","present":true},
                {"name":"checks","required":"read","granted":"none","present":false},
                {"name":"statuses","required":"read","granted":"read","present":true}],
                "warnings":[{"name":"workflows","granted":"write"}]}
                """),
            answer.Body);
        Assert.DoesNotContain("MIIE", answer.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_checks_the_stored_credential_and_answers_the_same_document()
    {
        var gateway = new FakeCodeHost();
        await using var host = await StartAsync(gateway);
        using var client = ClientFor(host);
        var connected = await client.SendAsync(HttpMethod.Put, "/api/github/app/123456", Key, Token);

        var answer = await client.GetAsync("/api/github/identity", Token);

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Equal(connected.Body, answer.Body);
        Assert.Equal(1, gateway.Checks);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1e3")]
    [InlineData("12.5")]
    [InlineData("99999999999999999999")]
    public async Task An_id_that_is_not_a_positive_whole_number_is_a_400_and_the_gateway_is_not_asked(string appId)
    {
        var gateway = new FakeCodeHost();
        await using var host = await StartAsync(gateway);
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/github/app/" + Uri.EscapeDataString(appId), Key, Token);

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal("urn:hephaestus:problem:github-app-id-invalid", answer.Problem!.Type);
        Assert.Empty(gateway.Connects);
    }

    [Theory]
    [InlineData("", "secret-value-empty")]
    [InlineData("short", "secret-value-too-short")]
    public async Task A_key_the_secret_policy_refuses_is_a_400_and_the_gateway_is_not_asked(string key, string slug)
    {
        var gateway = new FakeCodeHost();
        await using var host = await StartAsync(gateway);
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/github/app/1", key, Token);

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal($"urn:hephaestus:problem:{slug}", answer.Problem!.Type);
        Assert.Empty(gateway.Connects);
    }

    [Theory]
    [InlineData(GitHubIdentityFailure.NotConnected, HttpStatusCode.NotFound, "github-not-connected", "No GitHub App is connected")]
    [InlineData(GitHubIdentityFailure.KeyUnreadable, HttpStatusCode.BadRequest, "github-key-unreadable", "GitHub credential rejected")]
    [InlineData(GitHubIdentityFailure.Rejected, HttpStatusCode.UnprocessableEntity, "github-credential-rejected", "GitHub credential rejected")]
    [InlineData(GitHubIdentityFailure.Unreachable, HttpStatusCode.BadGateway, "github-unreachable", "GitHub is unreachable")]
    public async Task Each_failure_is_a_problem_with_a_stable_type_and_a_title_that_says_which_it_was(GitHubIdentityFailure failure, HttpStatusCode status, string slug, string title)
    {
        var gateway = new FakeCodeHost { Answer = GitHubIdentityResult.Failed(failure) };
        await using var host = await StartAsync(gateway);
        using var client = ClientFor(host);

        var put = await client.SendAsync(HttpMethod.Put, "/api/github/app/1", Key, Token);
        var get = await client.GetAsync("/api/github/identity", Token);

        foreach (var answer in new[] { put, get })
        {
            Assert.Equal(status, answer.Status);
            Assert.Equal($"urn:hephaestus:problem:{slug}", answer.Problem!.Type);
            Assert.Equal(title, answer.Problem.Title);
            Assert.DoesNotContain("MIIE", answer.Body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_rejected_credential_and_an_unreachable_GitHub_are_told_apart_in_every_language()
    {
        foreach (var language in new[] { Language.English, Language.Polish })
        {
            var rejected = new FakeCodeHost { Answer = GitHubIdentityResult.Failed(GitHubIdentityFailure.Rejected) };
            var unreachable = new FakeCodeHost { Answer = GitHubIdentityResult.Failed(GitHubIdentityFailure.Unreachable) };
            await using var first = await StartAsync(rejected, language);
            await using var second = await StartAsync(unreachable, language);
            using var firstClient = ClientFor(first, language);
            using var secondClient = ClientFor(second, language);

            var one = await firstClient.GetAsync("/api/github/identity", Token);
            var other = await secondClient.GetAsync("/api/github/identity", Token);

            Assert.NotEqual(one.Problem!.Title, other.Problem!.Title);
            Assert.NotEqual(one.Problem.Detail, other.Problem.Detail);
        }
    }

    [Fact]
    public async Task The_host_uses_the_GitHub_gateway_unless_a_test_replaces_it()
    {
        await using var host = await RunningHost.StartAsync();

        Assert.IsType<Hephaestus.GitHub.GitHubGateway>(host.App.Services.GetRequiredService<ICodeHost>());
    }

    [Fact]
    public async Task Checking_before_anything_was_connected_says_so_without_asking_GitHub()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.GetAsync("/api/github/identity", Token);

        Assert.Equal(HttpStatusCode.NotFound, answer.Status);
        Assert.Equal("urn:hephaestus:problem:github-not-connected", answer.Problem!.Type);
    }
}
