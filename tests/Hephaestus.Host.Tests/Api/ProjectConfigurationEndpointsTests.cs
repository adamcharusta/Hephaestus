using System.Net;
using System.Text;
using System.Text.Json;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Host.Cli;

namespace Hephaestus.Host.Tests.Api;

/// <summary>Story 1.11 on the control API: <c>GET /api/projects/check</c> reads the Base Branch's file and answers with what is wrong, in a document that is the same in every language.</summary>
public class ProjectConfigurationEndpointsTests
{
    private const string Commit = "3f2a9c1d5e7b8a0c4d6e8f0a1b2c3d4e5f607182";
    private const string Valid = "image: node:22\nvalidation:\n  - npm test\n";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FakeCodeHost GatewayWith(string? content)
    {
        var gateway = new FakeCodeHost();
        gateway.Files[(101, "main")] = (Commit, content is null ? null : Encoding.UTF8.GetBytes(content));
        return gateway;
    }

    private static async Task<RunningHost> StartRegisteredAsync(FakeCodeHost gateway, Language language = Language.English)
    {
        var host = await RunningHost.StartAsync(language, configure: builder => builder.Services.AddSingleton<ICodeHost>(gateway));
        using var client = new ControlApiClient(host.Settings.ControlSocketPath, language);
        var registered = await client.SendAsync(HttpMethod.Put, "/api/projects?name=acme/shop", """{"baseBranch":"main"}""", Token);
        Assert.Equal(HttpStatusCode.Created, registered.Status);
        return host;
    }

    private static ControlApiClient ClientFor(RunningHost host, Language language = Language.English) => new(host.Settings.ControlSocketPath, language);

    [Fact]
    public async Task A_valid_file_answers_valid_with_the_commit_and_the_settings()
    {
        await using var host = await StartRegisteredAsync(GatewayWith(Valid));
        using var client = ClientFor(host);

        var answer = await client.GetAsync("/api/projects/check?name=acme/shop", Token);

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Equal(
            $$$"""{"repositoryId":101,"name":"acme/shop","baseBranch":"main","status":"valid","commitId":"{{{Commit}}}","issues":[],"configuration":{"primaryAlias":null,"fallbackAlias":null,"effort":null,"methodology":"default","validation":["npm test"],"correctionRounds":2,"image":"node:22","limits":{"taskDuration":null,"cpus":null,"memory":null},"allowedEgressHosts":[],"language":null}}""",
            answer.Body);
    }

    [Fact]
    public async Task The_limits_of_a_valid_file_are_in_the_written_form_and_the_language_is_its_tag()
    {
        await using var host = await StartRegisteredAsync(GatewayWith(Valid + "limits:\n  taskDuration: 90m\n  cpus: 1.5\n  memory: 4g\nlanguage: pl\nallowedEgressHosts: [Registry.NPMJS.org]\n"));
        using var client = ClientFor(host);

        var answer = await client.GetAsync("/api/projects/check?name=acme/shop", Token);

        Assert.Contains("\"limits\":{\"taskDuration\":\"1h30m\",\"cpus\":\"1.5\",\"memory\":\"4g\"}", answer.Body, StringComparison.Ordinal);
        Assert.Contains("\"allowedEgressHosts\":[\"registry.npmjs.org\"],\"language\":\"pl\"", answer.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_file_answers_missing_with_the_commit_and_no_settings()
    {
        await using var host = await StartRegisteredAsync(GatewayWith(null));
        using var client = ClientFor(host);

        var answer = await client.GetAsync("/api/projects/check?name=acme/shop", Token);

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Equal($$$"""{"repositoryId":101,"name":"acme/shop","baseBranch":"main","status":"missing","commitId":"{{{Commit}}}","issues":[],"configuration":null}""", answer.Body);
    }

    [Fact]
    public async Task Every_invalid_setting_is_listed_as_a_message_id_with_parameters_and_never_as_prose()
    {
        await using var host = await StartRegisteredAsync(GatewayWith("image: node:22\nmethodology: nope\nlanguage: fr\nsurprise: 1\n"));
        using var client = ClientFor(host);

        var answer = await client.GetAsync("/api/projects/check?name=acme/shop", Token);

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        using var document = JsonDocument.Parse(answer.Body);
        var root = document.RootElement;
        Assert.Equal("invalid", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("configuration").ValueKind);
        var issues = root.GetProperty("issues").EnumerateArray().Select(i => (i.GetProperty("setting").GetString(), i.GetProperty("line") is { ValueKind: JsonValueKind.Number } line ? line.GetInt32() : (int?)null, i.GetProperty("id").GetString())).ToList();
        Assert.Equal(
            [
                ("methodology", (int?)2, MessageIds.ConfigIssueMethodologyUnavailable),
                ("language", 3, MessageIds.ConfigIssueLanguageInvalid),
                ("surprise", 4, MessageIds.ConfigIssueUnknownKey),
                ("validation", null, MessageIds.ConfigIssueValidationNone),
            ],
            issues);
        Assert.Equal(["nope", "default"], root.GetProperty("issues")[0].GetProperty("arguments").EnumerateArray().Select(a => a.GetString()));
    }

    [Fact]
    public async Task The_document_is_the_same_in_every_language()
    {
        await using var host = await StartRegisteredAsync(GatewayWith("methodology: nope\n"));
        using var english = ClientFor(host, Language.English);
        using var polish = ClientFor(host, Language.Polish);

        var inEnglish = await english.GetAsync("/api/projects/check?name=acme/shop", Token);
        var inPolish = await polish.GetAsync("/api/projects/check?name=acme/shop", Token);

        Assert.Equal(inEnglish.Body, inPolish.Body);
    }

    [Fact]
    public async Task The_file_is_checked_against_the_Aliases_and_limits_the_Owner_stored()
    {
        await using var host = await StartRegisteredAsync(GatewayWith(Valid + "primaryAlias: opus5.5\nfallbackAlias: sonnet5.5\nlimits:\n  taskDuration: 5h\n"));
        using var client = ClientFor(host);
        await client.SendAsync(HttpMethod.Put, "/api/aliases/opus5.5", """{"agent":"claude-code","model":"claude-opus-5-5"}""", Token);
        await client.SendAsync(HttpMethod.Put, "/api/aliases/sonnet5.5", """{"agent":"claude-code","model":"claude-sonnet-5-5"}""", Token);
        await client.SendAsync(HttpMethod.Put, "/api/limits", """{"taskDuration":"2h"}""", Token);

        var answer = await client.GetAsync("/api/projects/check?name=acme/shop", Token);

        using var document = JsonDocument.Parse(answer.Body);
        Assert.Equal(
            [MessageIds.ConfigIssueAgentsSame, MessageIds.ConfigIssueLimitAboveInstallation],
            document.RootElement.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Only_the_Base_Branch_is_asked_for()
    {
        var gateway = GatewayWith(Valid);
        gateway.Files[(101, "feature")] = ("ffff", Encoding.UTF8.GetBytes("image: evil\n"));
        await using var host = await StartRegisteredAsync(gateway);
        using var client = ClientFor(host);

        var answer = await client.GetAsync("/api/projects/check?name=acme/shop", Token);

        Assert.Contains(Commit, answer.Body, StringComparison.Ordinal);
        Assert.Equal([(101L, "main", "hephaestus.yml")], gateway.FileReads);
    }

    [Fact]
    public async Task A_name_that_is_not_a_registered_Project_is_a_404_problem_and_GitHub_is_not_read()
    {
        var gateway = GatewayWith(Valid);
        await using var host = await StartRegisteredAsync(gateway);
        using var client = ClientFor(host);

        var answer = await client.GetAsync("/api/projects/check?name=acme/api", Token);

        Assert.Equal(HttpStatusCode.NotFound, answer.Status);
        Assert.Equal("urn:hephaestus:problem:project-not-registered", answer.Problem!.Type);
        Assert.Empty(gateway.FileReads);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?name=shop")]
    [InlineData("?name=../secrets/x")]
    [InlineData("?name=a/b/c")]
    public async Task A_name_that_is_missing_or_not_owner_slash_repo_is_a_400_problem(string query)
    {
        await using var host = await StartRegisteredAsync(GatewayWith(Valid));
        using var client = ClientFor(host);

        var answer = await client.GetAsync("/api/projects/check" + query, Token);

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal("urn:hephaestus:problem:project-name-invalid", answer.Problem!.Type);
    }

    [Theory]
    [InlineData(GitHubFileFailure.NotInstalled, HttpStatusCode.NotFound, "project-not-installed")]
    [InlineData(GitHubFileFailure.BranchNotFound, HttpStatusCode.NotFound, "project-branch-not-found")]
    [InlineData(GitHubFileFailure.NotConnected, HttpStatusCode.NotFound, "github-not-connected")]
    [InlineData(GitHubFileFailure.KeyUnreadable, HttpStatusCode.BadRequest, "github-key-unreadable")]
    [InlineData(GitHubFileFailure.Rejected, HttpStatusCode.UnprocessableEntity, "github-credential-rejected")]
    [InlineData(GitHubFileFailure.Unreachable, HttpStatusCode.BadGateway, "github-unreachable")]
    public async Task A_read_that_fails_is_the_problem_of_the_same_kind_as_when_registering(GitHubFileFailure failure, HttpStatusCode status, string slug)
    {
        var gateway = GatewayWith(Valid);
        await using var host = await StartRegisteredAsync(gateway);
        gateway.FileReadFails = failure;
        using var client = ClientFor(host);

        var answer = await client.GetAsync("/api/projects/check?name=acme/shop", Token);

        Assert.Equal(status, answer.Status);
        Assert.Equal("urn:hephaestus:problem:" + slug, answer.Problem!.Type);
    }

    [Fact]
    public async Task The_branch_that_is_gone_is_named_in_the_problem()
    {
        var gateway = GatewayWith(Valid);
        await using var host = await StartRegisteredAsync(gateway);
        gateway.FileReadFails = GitHubFileFailure.BranchNotFound;
        using var client = ClientFor(host);

        var answer = await client.GetAsync("/api/projects/check?name=acme/shop", Token);

        Assert.Equal("acme/shop has no branch named main. Nothing was changed.", answer.Problem!.Detail);
    }

    [Fact]
    public async Task A_check_changes_nothing_and_is_not_a_write()
    {
        await using var host = await StartRegisteredAsync(GatewayWith("this: [is not valid"));
        using var client = ClientFor(host);
        var before = (await client.GetAsync("/api/projects?all=true", Token)).Body;

        await client.GetAsync("/api/projects/check?name=acme/shop", Token);
        var post = await client.SendAsync(HttpMethod.Post, "/api/projects/check?name=acme/shop", null, Token);

        Assert.Equal(before, (await client.GetAsync("/api/projects?all=true", Token)).Body);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.Status);
    }
}
