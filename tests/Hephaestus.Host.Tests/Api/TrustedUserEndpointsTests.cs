using System.Net;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Trust;
using Hephaestus.Host.Cli;

namespace Hephaestus.Host.Tests.Api;

/// <summary>Story 1.8 on the control API: the Trusted User endpoints, their documents, and the problems they answer.</summary>
public class TrustedUserEndpointsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task<RunningHost> StartAsync(FakeCodeHost gateway, Language language = Language.English) =>
        RunningHost.StartAsync(language, configure: builder => builder.Services.AddSingleton<ICodeHost>(gateway));

    private static ControlApiClient ClientFor(RunningHost host, Language language = Language.English) => new(host.Settings.ControlSocketPath, language);

    [Fact]
    public async Task Put_stores_the_id_GitHub_names_answers_201_with_the_document_and_a_second_put_answers_200()
    {
        var gateway = new FakeCodeHost();
        await using var host = await StartAsync(gateway);
        using var client = ClientFor(host);

        var first = await client.SendAsync(HttpMethod.Put, "/api/trusted-users/alice", body: null, Token);
        var second = await client.SendAsync(HttpMethod.Put, "/api/trusted-users/ALICE", body: null, Token);

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Matches("""^\{"userId":583231,"login":"Alice","addedAt":"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ"\}$""", first.Body);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(first.Body, second.Body);
        Assert.Equal(["alice", "ALICE"], gateway.Lookups);
        Assert.Equal([583231L], (await host.App.Services.GetRequiredService<IReadStore>().ListTrustedUsersAsync(Token)).Select(u => u.UserId));
    }

    [Fact]
    public async Task Get_lists_the_users_by_login_with_their_ids_and_an_empty_list_is_an_empty_array()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        using var client = ClientFor(host);

        var none = await client.GetAsync("/api/trusted-users", Token);
        await client.SendAsync(HttpMethod.Put, "/api/trusted-users/bob", body: null, Token);
        await client.SendAsync(HttpMethod.Put, "/api/trusted-users/alice", body: null, Token);
        var two = await client.GetAsync("/api/trusted-users", Token);

        Assert.Equal("""{"trustedUsers":[]}""", none.Body);
        Assert.Matches("""^\{"trustedUsers":\[\{"userId":583231,"login":"Alice","addedAt":"[^"]+"\},\{"userId":1001,"login":"bob","addedAt":"[^"]+"\}\]\}$""", two.Body);
    }

    [Fact]
    public async Task Delete_removes_the_user_answers_who_and_a_later_get_omits_them()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        using var client = ClientFor(host);
        await client.SendAsync(HttpMethod.Put, "/api/trusted-users/alice", body: null, Token);

        var removed = await client.SendAsync(HttpMethod.Delete, "/api/trusted-users/alice", body: null, Token);
        var list = await client.GetAsync("/api/trusted-users", Token);

        Assert.Equal(HttpStatusCode.OK, removed.Status);
        Assert.Contains("\"userId\":583231", removed.Body, StringComparison.Ordinal);
        Assert.Equal("""{"trustedUsers":[]}""", list.Body);
    }

    [Theory]
    [InlineData("nobody", HttpStatusCode.NotFound, "github-user-not-found", "GitHub user not found")]
    [InlineData("hephaestus-test%5Bbot%5D", HttpStatusCode.UnprocessableEntity, "trusted-user-own-identity", "Hephaestus cannot trust itself")]
    [InlineData("-bad", HttpStatusCode.BadRequest, "trusted-user-login-invalid", "Invalid GitHub login")]
    [InlineData("a%20b", HttpStatusCode.BadRequest, "trusted-user-login-invalid", "Invalid GitHub login")]
    public async Task A_login_that_cannot_be_added_is_a_problem_that_says_why_and_nothing_is_stored(string path, HttpStatusCode status, string slug, string title)
    {
        await using var host = await StartAsync(new FakeCodeHost());
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/trusted-users/" + path, body: null, Token);

        Assert.Equal(status, answer.Status);
        Assert.Equal($"urn:hephaestus:problem:{slug}", answer.Problem!.Type);
        Assert.Equal(title, answer.Problem.Title);
        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListTrustedUsersAsync(Token));
    }

    [Fact]
    public async Task The_own_identity_problem_names_the_login_and_the_unknown_user_problem_names_it_too()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        using var client = ClientFor(host);

        var own = await client.SendAsync(HttpMethod.Put, "/api/trusted-users/hephaestus-test%5Bbot%5D", body: null, Token);
        var unknown = await client.SendAsync(HttpMethod.Put, "/api/trusted-users/nobody", body: null, Token);

        Assert.Contains("hephaestus-test[bot]", own.Problem!.Detail, StringComparison.Ordinal);
        Assert.Contains("never a Trusted User", own.Problem.Detail, StringComparison.Ordinal);
        Assert.Contains("nobody", unknown.Problem!.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(GitHubUserFailure.NotConnected, HttpStatusCode.NotFound, "github-not-connected", "No GitHub App is connected")]
    [InlineData(GitHubUserFailure.KeyUnreadable, HttpStatusCode.BadRequest, "github-key-unreadable", "GitHub credential rejected")]
    [InlineData(GitHubUserFailure.Rejected, HttpStatusCode.UnprocessableEntity, "github-credential-rejected", "GitHub credential rejected")]
    [InlineData(GitHubUserFailure.Unreachable, HttpStatusCode.BadGateway, "github-unreachable", "GitHub is unreachable")]
    public async Task A_lookup_that_fails_is_the_problem_of_the_GitHub_identity_stories(GitHubUserFailure lookup, HttpStatusCode status, string slug, string title)
    {
        var gateway = new FakeCodeHost { LookupFails = lookup };
        await using var host = await StartAsync(gateway);
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/trusted-users/alice", body: null, Token);

        Assert.Equal(status, answer.Status);
        Assert.Equal($"urn:hephaestus:problem:{slug}", answer.Problem!.Type);
        Assert.Equal(title, answer.Problem.Title);
    }

    [Fact]
    public async Task Revoking_trust_does_not_need_GitHub_but_resolving_an_unlisted_login_does()
    {
        var gateway = new FakeCodeHost();
        await using var host = await StartAsync(gateway);
        using var client = ClientFor(host);
        await client.SendAsync(HttpMethod.Put, "/api/trusted-users/alice", body: null, Token);
        await client.SendAsync(HttpMethod.Put, "/api/trusted-users/bob", body: null, Token);
        await client.SendAsync(HttpMethod.Delete, "/api/trusted-users/bob", body: null, Token);
        gateway.Lookups.Clear();
        gateway.LookupFails = GitHubUserFailure.Unreachable;

        var unknown = await client.SendAsync(HttpMethod.Delete, "/api/trusted-users/carol", body: null, Token);
        var revoked = await client.SendAsync(HttpMethod.Delete, "/api/trusted-users/alice", body: null, Token);

        Assert.Equal(HttpStatusCode.BadGateway, unknown.Status);
        Assert.Equal(HttpStatusCode.OK, revoked.Status);
        Assert.Equal(["carol"], gateway.Lookups);
        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListTrustedUsersAsync(Token));
    }

    [Fact]
    public async Task Removing_someone_GitHub_does_not_know_is_not_a_Trusted_User()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Delete, "/api/trusted-users/nobody", body: null, Token);

        Assert.Equal(HttpStatusCode.NotFound, answer.Status);
        Assert.Equal("urn:hephaestus:problem:trusted-user-not-found", answer.Problem!.Type);
        Assert.Equal("Trusted User not found", answer.Problem.Title);
    }

    [Fact]
    public async Task The_problems_are_in_the_language_of_the_request()
    {
        await using var host = await StartAsync(new FakeCodeHost(), Language.Polish);
        using var client = ClientFor(host, Language.Polish);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/trusted-users/nobody", body: null, Token);

        Assert.Equal("Nie znaleziono użytkownika GitHuba", answer.Problem!.Title);
        Assert.Contains("nobody", answer.Problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Other_methods_on_a_Trusted_User_are_refused()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        using var client = ClientFor(host);

        var post = await client.SendAsync(HttpMethod.Post, "/api/trusted-users/alice", body: null, Token);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.Status);
    }
}
