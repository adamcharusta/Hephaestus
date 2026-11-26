using System.Net;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Host.Cli;

namespace Hephaestus.Host.Tests.Api;

/// <summary>Story 1.10 on the control API: the Project endpoints, their documents, and the problems they answer.</summary>
public class ProjectEndpointsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private const string Main = """{"baseBranch":"main"}""";

    private static Task<RunningHost> StartAsync(FakeCodeHost gateway, Language language = Language.English) =>
        RunningHost.StartAsync(language, configure: builder => builder.Services.AddSingleton<ICodeHost>(gateway));

    private static ControlApiClient ClientFor(RunningHost host, Language language = Language.English) => new(host.Settings.ControlSocketPath, language);

    private static Task<IReadOnlyList<Project>> StoredAsync(RunningHost host, bool includeRemoved = true) =>
        host.App.Services.GetRequiredService<IReadStore>().ListProjectsAsync(includeRemoved, Token);

    [Fact]
    public async Task Put_registers_by_repository_id_answers_201_with_the_document_and_a_second_put_answers_200()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        using var client = ClientFor(host);

        var first = await client.SendAsync(HttpMethod.Put, "/api/projects?name=acme/shop", Main, Token);
        var second = await client.SendAsync(HttpMethod.Put, "/api/projects?name=acme/shop", Main, Token);

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Matches("""^\{"repositoryId":101,"name":"acme/shop","baseBranch":"main","registeredAt":"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ","removedAt":null,"change":"registered","baseBranchProtected":true\}$""", first.Body);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Contains("\"change\":\"unchanged\"", second.Body, StringComparison.Ordinal);
        Assert.Equal([101L], (await StoredAsync(host)).Select(p => p.RepositoryId));
    }

    [Fact]
    public async Task An_unprotected_Base_Branch_is_reported_as_not_protected()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/projects?name=acme/shop", """{"baseBranch":"develop"}""", Token);

        Assert.Equal(HttpStatusCode.Created, answer.Status);
        Assert.Contains("\"baseBranchProtected\":false", answer.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Base_Branch_with_a_slash_travels_in_the_body_and_is_stored()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/projects?name=acme/shop", """{"baseBranch":"release/1.0"}""", Token);

        Assert.Equal(HttpStatusCode.Created, answer.Status);
        Assert.Equal("release/1.0", Assert.Single(await StoredAsync(host)).BaseBranch);
    }

    [Fact]
    public async Task The_name_is_stored_as_GitHub_spells_it_whatever_was_typed()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        using var client = ClientFor(host);

        await client.SendAsync(HttpMethod.Put, "/api/projects?name=ACME/Shop", Main, Token);

        Assert.Equal("acme/shop", Assert.Single(await StoredAsync(host)).FullName);
    }

    [Fact]
    public async Task Get_lists_the_Projects_by_name_and_an_empty_list_is_an_empty_array()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        using var client = ClientFor(host);

        var none = await client.GetAsync("/api/projects", Token);
        await client.SendAsync(HttpMethod.Put, "/api/projects?name=acme/shop", Main, Token);
        await client.SendAsync(HttpMethod.Put, "/api/projects?name=acme/api", Main, Token);
        var two = await client.GetAsync("/api/projects", Token);

        Assert.Equal("""{"projects":[]}""", none.Body);
        Assert.Matches("""^\{"projects":\[\{"repositoryId":102,"name":"acme/api",.*\},\{"repositoryId":101,"name":"acme/shop",.*\}\]\}$""", two.Body);
    }

    [Fact]
    public async Task Delete_retires_the_Project_and_it_is_listed_only_when_removed_Projects_are_asked_for()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        using var client = ClientFor(host);
        await client.SendAsync(HttpMethod.Put, "/api/projects?name=acme/shop", Main, Token);

        var removed = await client.SendAsync(HttpMethod.Delete, "/api/projects?name=acme/shop", body: null, Token);
        var active = await client.GetAsync("/api/projects", Token);
        var all = await client.GetAsync("/api/projects?all=true", Token);

        Assert.Equal(HttpStatusCode.OK, removed.Status);
        Assert.Matches("""^\{"repositoryId":101,"name":"acme/shop","baseBranch":"main","registeredAt":"[^"]+","removedAt":"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ","change":null,"baseBranchProtected":null\}$""", removed.Body);
        Assert.Equal("""{"projects":[]}""", active.Body);
        Assert.Contains("\"removedAt\":\"20", all.Body, StringComparison.Ordinal);
        Assert.Single(await StoredAsync(host));
        Assert.Empty(await StoredAsync(host, includeRemoved: false));
    }

    [Fact]
    public async Task A_renamed_repository_is_still_listed_by_its_id_and_is_removed_under_its_new_name()
    {
        var gateway = new FakeCodeHost();
        await using var host = await StartAsync(gateway);
        using var client = ClientFor(host);
        await client.SendAsync(HttpMethod.Put, "/api/projects?name=acme/shop", Main, Token);
        gateway.Repositories.Remove("acme/shop");
        gateway.Repositories["acme/store"] = (101, "acme/store", new Dictionary<string, bool> { ["main"] = true });

        var list = await client.GetAsync("/api/projects", Token);
        var removed = await client.SendAsync(HttpMethod.Delete, "/api/projects?name=acme/store", body: null, Token);

        Assert.Contains("\"repositoryId\":101", list.Body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, removed.Status);
        Assert.Empty(await StoredAsync(host, includeRemoved: false));
    }

    [Theory]
    [InlineData("acme/other", Main, HttpStatusCode.NotFound, "project-not-installed", "The GitHub App is not installed on the repository")]
    [InlineData("acme/shop", """{"baseBranch":"nope"}""", HttpStatusCode.NotFound, "project-branch-not-found", "The Base Branch does not exist")]
    [InlineData("acme/shop", """{"baseBranch":"a..b"}""", HttpStatusCode.BadRequest, "project-branch-invalid", "Invalid Base Branch")]
    [InlineData("acme/shop", """{"baseBranch":null}""", HttpStatusCode.BadRequest, "project-branch-invalid", "Invalid Base Branch")]
    [InlineData("acme/shop", "{}", HttpStatusCode.BadRequest, "project-branch-invalid", "Invalid Base Branch")]
    [InlineData("acme/-shop%20x", Main, HttpStatusCode.BadRequest, "project-name-invalid", "Invalid repository name")]
    [InlineData("-acme/shop", Main, HttpStatusCode.BadRequest, "project-name-invalid", "Invalid repository name")]
    [InlineData("acme/shop", "not json", HttpStatusCode.BadRequest, "request-invalid", "Request not understood")]
    [InlineData("acme/shop", "", HttpStatusCode.BadRequest, "request-invalid", "Request not understood")]
    public async Task A_registration_that_cannot_happen_is_a_problem_that_says_why_and_nothing_is_stored(string path, string body, HttpStatusCode status, string slug, string title)
    {
        await using var host = await StartAsync(new FakeCodeHost());
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/projects?name=" + path, body, Token);

        Assert.Equal(status, answer.Status);
        Assert.Equal($"urn:hephaestus:problem:{slug}", answer.Problem!.Type);
        Assert.Equal(title, answer.Problem.Title);
        Assert.Empty(await StoredAsync(host));
    }

    [Fact]
    public async Task The_problems_name_the_repository_and_the_branch()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        using var client = ClientFor(host);

        var installed = await client.SendAsync(HttpMethod.Put, "/api/projects?name=acme/other", Main, Token);
        var branch = await client.SendAsync(HttpMethod.Put, "/api/projects?name=acme/shop", """{"baseBranch":"nope"}""", Token);

        Assert.Contains("not installed on acme/other", installed.Problem!.Detail, StringComparison.Ordinal);
        Assert.Equal("acme/shop has no branch named nope. Nothing was changed.", branch.Problem!.Detail);
    }

    [Theory]
    [InlineData(GitHubRepositoryFailure.NotConnected, HttpStatusCode.NotFound, "github-not-connected", "No GitHub App is connected")]
    [InlineData(GitHubRepositoryFailure.KeyUnreadable, HttpStatusCode.BadRequest, "github-key-unreadable", "GitHub credential rejected")]
    [InlineData(GitHubRepositoryFailure.Rejected, HttpStatusCode.UnprocessableEntity, "github-credential-rejected", "GitHub credential rejected")]
    [InlineData(GitHubRepositoryFailure.Unreachable, HttpStatusCode.BadGateway, "github-unreachable", "GitHub is unreachable")]
    public async Task A_lookup_that_fails_is_the_problem_of_the_GitHub_identity_stories(GitHubRepositoryFailure lookup, HttpStatusCode status, string slug, string title)
    {
        await using var host = await StartAsync(new FakeCodeHost { RepositoryLookupFails = lookup });
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/projects?name=acme/shop", Main, Token);

        Assert.Equal(status, answer.Status);
        Assert.Equal($"urn:hephaestus:problem:{slug}", answer.Problem!.Type);
        Assert.Equal(title, answer.Problem.Title);
        Assert.Empty(await StoredAsync(host));
    }

    [Fact]
    public async Task Removing_a_name_that_is_not_registered_is_a_problem_and_removing_one_that_is_malformed_is_another()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        using var client = ClientFor(host);
        await client.SendAsync(HttpMethod.Put, "/api/projects?name=acme/shop", Main, Token);

        var unknown = await client.SendAsync(HttpMethod.Delete, "/api/projects?name=acme/unknown", body: null, Token);
        var malformed = await client.SendAsync(HttpMethod.Delete, "/api/projects?name=acme/sh%20op", body: null, Token);

        Assert.Equal(HttpStatusCode.NotFound, unknown.Status);
        Assert.Equal("urn:hephaestus:problem:project-not-registered", unknown.Problem!.Type);
        Assert.Contains("acme/unknown is not a registered Project", unknown.Problem.Detail, StringComparison.Ordinal);
        Assert.Equal("urn:hephaestus:problem:project-name-invalid", malformed.Problem!.Type);
    }

    [Fact]
    public async Task Revoking_a_Project_does_not_need_GitHub_but_resolving_an_unlisted_name_does()
    {
        var gateway = new FakeCodeHost();
        await using var host = await StartAsync(gateway);
        using var client = ClientFor(host);
        await client.SendAsync(HttpMethod.Put, "/api/projects?name=acme/shop", Main, Token);
        gateway.RepositoryLookups.Clear();
        gateway.RepositoryLookupFails = GitHubRepositoryFailure.Unreachable;

        var unlisted = await client.SendAsync(HttpMethod.Delete, "/api/projects?name=acme/store", body: null, Token);
        var removed = await client.SendAsync(HttpMethod.Delete, "/api/projects?name=acme/shop", body: null, Token);

        Assert.Equal(HttpStatusCode.BadGateway, unlisted.Status);
        Assert.Equal(HttpStatusCode.OK, removed.Status);
        Assert.Equal([("acme/store", (string?)null)], gateway.RepositoryLookups);
    }

    [Fact]
    public async Task The_problems_are_in_the_language_of_the_request()
    {
        await using var host = await StartAsync(new FakeCodeHost(), Language.Polish);
        using var client = ClientFor(host, Language.Polish);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/projects?name=acme/other", Main, Token);

        Assert.Equal("Aplikacja GitHub nie jest zainstalowana na repozytorium", answer.Problem!.Title);
        Assert.Contains("acme/other", answer.Problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Other_methods_on_a_Project_are_refused()
    {
        await using var host = await StartAsync(new FakeCodeHost());
        using var client = ClientFor(host);

        var post = await client.SendAsync(HttpMethod.Post, "/api/projects?name=acme/shop", Main, Token);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?name=")]
    [InlineData("?name=..")]
    [InlineData("?name=../secrets/token")]
    [InlineData("?name=acme/shop/extra")]
    [InlineData("?other=acme/shop")]
    public async Task A_missing_or_malformed_name_is_refused_as_a_name_by_both_verbs(string query)
    {
        await using var host = await StartAsync(new FakeCodeHost());
        using var client = ClientFor(host);

        var put = await client.SendAsync(HttpMethod.Put, "/api/projects" + query, Main, Token);
        var delete = await client.SendAsync(HttpMethod.Delete, "/api/projects" + query, body: null, Token);

        Assert.Equal("urn:hephaestus:problem:project-name-invalid", put.Problem!.Type);
        Assert.Equal("urn:hephaestus:problem:project-name-invalid", delete.Problem!.Type);
        Assert.Empty(await StoredAsync(host));
    }
}
