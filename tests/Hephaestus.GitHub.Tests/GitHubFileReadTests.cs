using System.Net;
using System.Security.Cryptography;
using System.Text;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Projects;

namespace Hephaestus.GitHub.Tests;

/// <summary>Story 1.11, AD-16: the gateway returns the raw bytes of a file of the Base Branch with the commit id they belong to, and parses nothing.</summary>
public class GitHubFileReadTests
{
    private const long AppId = 123456;
    private const string KeyName = GitHubAppCredential.PrivateKeySecretName;
    private const string Main = "1111111111111111111111111111111111111111";
    private const int Maximum = 64 * 1024;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FakeStores Connected() => new() { AppId = AppId, Secrets = { [KeyName] = RSA.Create(2048).ExportRSAPrivateKeyPem() } };

    private static GitHubGateway GatewayOver(FakeStores stores, FakeGitHub github, TimeSpan? deadline = null) =>
        new(stores, stores, stores, new FixedTime(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero)), () => github, deadline);

    private static FakeGitHub WithFile(string content = "image: node:22\n", string commit = Main)
    {
        var github = new FakeGitHub();
        github.Commits["1/main"] = commit;
        github.Files[$"1/hephaestus.yml@{commit}"] = Encoding.UTF8.GetBytes(content);
        return github;
    }

    [Fact]
    public async Task The_file_comes_back_as_its_raw_bytes_with_the_commit_at_the_tip_of_the_branch()
    {
        var result = await GatewayOver(Connected(), WithFile("image: node:22\n")).ReadFileAsync(1, "main", "hephaestus.yml", Maximum, Token);

        Assert.Equal(GitHubFileFailure.None, result.Failure);
        Assert.Equal("image: node:22\n", Encoding.UTF8.GetString(result.File!.Content));
        Assert.Equal(Main, result.File.CommitId);
        Assert.Equal(Main, result.CommitId);
    }

    [Fact]
    public async Task The_bytes_are_not_decoded_or_changed_so_a_byte_order_mark_a_carriage_return_and_invalid_UTF8_arrive_as_they_are()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, (byte)'a', (byte)':', (byte)' ', (byte)'1', (byte)'\r', (byte)'\n', 0xFF, 0xC3, 0x28];
        var github = new FakeGitHub();
        github.Commits["1/main"] = Main;
        github.Files[$"1/hephaestus.yml@{Main}"] = bytes;

        var result = await GatewayOver(Connected(), github).ReadFileAsync(1, "main", "hephaestus.yml", Maximum, Token);

        Assert.Equal(bytes, result.File!.Content);
    }

    [Fact]
    public async Task The_file_is_asked_for_at_the_commit_id_of_the_tip_and_never_at_a_branch_name_so_another_branch_is_not_read()
    {
        const string other = "2222222222222222222222222222222222222222";
        var github = WithFile("image: node:22\n");
        github.Branches["1/feature"] = false;
        github.Commits["1/feature"] = other;
        github.Files[$"1/hephaestus.yml@{other}"] = Encoding.UTF8.GetBytes("image: evil\n");

        var result = await GatewayOver(Connected(), github).ReadFileAsync(1, "main", "hephaestus.yml", Maximum, Token);

        Assert.Equal("image: node:22\n", Encoding.UTF8.GetString(result.File!.Content));
        Assert.Equal([Main], github.ContentRefs);
        Assert.DoesNotContain(github.Requests, r => r.Path.Contains("/branches/feature", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_branch_without_the_file_is_not_found_and_the_commit_that_was_looked_at_is_named()
    {
        var github = new FakeGitHub();
        github.Commits["1/main"] = Main;

        var result = await GatewayOver(Connected(), github).ReadFileAsync(1, "main", "hephaestus.yml", Maximum, Token);

        Assert.Equal(GitHubFileFailure.NotFound, result.Failure);
        Assert.Null(result.File);
        Assert.Equal(Main, result.CommitId);
    }

    [Theory]
    [InlineData("""[{"type":"file","name":"a","path":"hephaestus.yml/a","size":1,"sha":"x"},{"type":"file","name":"b","path":"hephaestus.yml/b","size":1,"sha":"y"}]""")]
    [InlineData("""{"type":"symlink","name":"hephaestus.yml","path":"hephaestus.yml","size":5,"sha":"x","target":"other.yml"}""")]
    [InlineData("""{"type":"submodule","name":"hephaestus.yml","path":"hephaestus.yml","size":0,"sha":"x","submodule_git_url":"https://github.com/a/b"}""")]
    public async Task A_directory_a_link_or_a_submodule_is_not_the_file(string body)
    {
        var github = new FakeGitHub { Override = r => r.RequestUri!.AbsolutePath.Contains("/contents/", StringComparison.Ordinal) ? FakeGitHub.Json(HttpStatusCode.OK, body) : null };
        github.Commits["1/main"] = Main;

        var result = await GatewayOver(Connected(), github).ReadFileAsync(1, "main", "hephaestus.yml", Maximum, Token);

        Assert.Equal(GitHubFileFailure.NotFound, result.Failure);
        Assert.Equal(Main, result.CommitId);
    }

    [Fact]
    public async Task A_file_larger_than_the_bound_is_not_read_and_the_commit_is_named()
    {
        var result = await GatewayOver(Connected(), WithFile(new string('x', Maximum + 1))).ReadFileAsync(1, "main", "hephaestus.yml", Maximum, Token);

        Assert.Equal(GitHubFileFailure.TooLarge, result.Failure);
        Assert.Null(result.File);
        Assert.Equal(Main, result.CommitId);
    }

    [Fact]
    public async Task A_file_exactly_at_the_bound_is_read()
    {
        var result = await GatewayOver(Connected(), WithFile(new string('#', Maximum))).ReadFileAsync(1, "main", "hephaestus.yml", Maximum, Token);

        Assert.Equal(Maximum, result.File!.Content.Length);
    }

    [Fact]
    public async Task A_content_that_is_not_base64_is_unreachable_not_a_crash()
    {
        var github = new FakeGitHub
        {
            Override = r => r.RequestUri!.AbsolutePath.Contains("/contents/", StringComparison.Ordinal)
                ? FakeGitHub.Json(HttpStatusCode.OK, """{"type":"file","encoding":"base64","size":3,"name":"hephaestus.yml","path":"hephaestus.yml","sha":"x","content":"%%%"}""")
                : null,
        };

        Assert.Equal(GitHubFileFailure.Unreachable, (await GatewayOver(Connected(), github).ReadFileAsync(1, "main", "hephaestus.yml", Maximum, Token)).Failure);
    }

    [Fact]
    public async Task A_branch_the_repository_does_not_have_is_not_found()
    {
        var result = await GatewayOver(Connected(), WithFile()).ReadFileAsync(1, "gone", "hephaestus.yml", Maximum, Token);

        Assert.Equal(GitHubFileFailure.BranchNotFound, result.Failure);
        Assert.Null(result.CommitId);
    }

    [Fact]
    public async Task The_repository_is_found_by_its_id_in_whichever_installation_holds_it_and_not_by_a_name()
    {
        var github = new FakeGitHub();
        github.Repositories[10] = ["acme/web"];
        github.Repositories[11] = ["someone/first", "someone/renamed"];
        github.Branches["2/main"] = true;
        github.Commits["2/main"] = Main;
        github.Files[$"2/hephaestus.yml@{Main}"] = Encoding.UTF8.GetBytes("image: x\n");

        var result = await GatewayOver(Connected(), github).ReadFileAsync(2, "main", "hephaestus.yml", Maximum, Token);

        Assert.Equal(GitHubFileFailure.None, result.Failure);
        Assert.Equal(["/app/installations/10/access_tokens", "/app/installations/11/access_tokens"], github.Requests.Where(r => r.Path.EndsWith("/access_tokens", StringComparison.Ordinal)).Select(r => r.Path));
    }

    [Fact]
    public async Task A_repository_no_installation_holds_is_not_installed()
    {
        var result = await GatewayOver(Connected(), WithFile()).ReadFileAsync(99, "main", "hephaestus.yml", Maximum, Token);

        Assert.Equal(GitHubFileFailure.NotInstalled, result.Failure);
    }

    [Fact]
    public async Task A_suspended_installation_reaches_no_file()
    {
        var github = new FakeGitHub
        {
            InstallationsJson = $$"""[{"id":10,"account":{"login":"acme","id":2,"type":"Organization"},"target_type":"Organization","repository_selection":"all","suspended_at":"2026-11-01T00:00:00Z","permissions":{{FakeGitHub.Permissions}}}]""",
        };

        var result = await GatewayOver(Connected(), github).ReadFileAsync(1, "main", "hephaestus.yml", Maximum, Token);

        Assert.Equal(GitHubFileFailure.NotInstalled, result.Failure);
        Assert.DoesNotContain(github.Requests, r => r.Path.EndsWith("/access_tokens", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nothing_stored_means_not_connected_and_GitHub_is_not_asked()
    {
        var github = new FakeGitHub();

        Assert.Equal(GitHubFileFailure.NotConnected, (await GatewayOver(new FakeStores(), github).ReadFileAsync(1, "main", "hephaestus.yml", Maximum, Token)).Failure);
        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task A_stored_key_that_is_not_a_key_is_unreadable_and_GitHub_is_not_asked()
    {
        var github = new FakeGitHub();
        var stores = new FakeStores { AppId = AppId, Secrets = { [KeyName] = "not a pem key at all" } };

        Assert.Equal(GitHubFileFailure.KeyUnreadable, (await GatewayOver(stores, github).ReadFileAsync(1, "main", "hephaestus.yml", Maximum, Token)).Failure);
        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task A_credential_GitHub_refuses_is_rejected()
    {
        var github = new FakeGitHub { Override = r => r.RequestUri!.AbsolutePath == "/app/installations" ? FakeGitHub.Json(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""") : null };

        Assert.Equal(GitHubFileFailure.Rejected, (await GatewayOver(Connected(), github).ReadFileAsync(1, "main", "hephaestus.yml", Maximum, Token)).Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task GitHub_failing_to_serve_the_contents_is_unreachable(HttpStatusCode status)
    {
        var github = WithFile();
        github.Override = r => r.RequestUri!.AbsolutePath.Contains("/contents/", StringComparison.Ordinal) ? FakeGitHub.Json(status, """{"message":"x"}""") : null;

        Assert.Equal(GitHubFileFailure.Unreachable, (await GatewayOver(Connected(), github).ReadFileAsync(1, "main", "hephaestus.yml", Maximum, Token)).Failure);
    }

    [Fact]
    public async Task A_read_that_outlasts_its_deadline_is_unreachable()
    {
        var result = await GatewayOver(Connected(), new FakeGitHub { Hang = true }, TimeSpan.FromMilliseconds(200)).ReadFileAsync(1, "main", "hephaestus.yml", Maximum, Token);

        Assert.Equal(GitHubFileFailure.Unreachable, result.Failure);
    }

    [Fact]
    public async Task A_read_cancelled_by_the_caller_is_cancelled_not_reported_as_unreachable()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GatewayOver(Connected(), WithFile()).ReadFileAsync(1, "main", "hephaestus.yml", Maximum, cancelled.Token));
    }

    [Fact]
    public async Task The_read_is_made_as_the_App_then_as_the_installation_and_only_reads()
    {
        var github = WithFile();

        await GatewayOver(Connected(), github).ReadFileAsync(1, "main", "hephaestus.yml", Maximum, Token);

        Assert.StartsWith("Bearer ", github.Requests.Single(r => r.Path == "/app/installations").Authorization, StringComparison.Ordinal);
        Assert.EndsWith(FakeGitHub.InstallationToken, github.Requests.Single(r => r.Path.Contains("/contents/", StringComparison.Ordinal)).Authorization, StringComparison.Ordinal);
        Assert.All(github.Requests, r => Assert.Contains(r.Method.Method, new[] { "GET", "POST" }));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
