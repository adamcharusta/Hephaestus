using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Host.Cli;

namespace Hephaestus.Host.Tests.Api;

/// <summary>Story 1.9 on the control API: the Alias, limit, protected path, setting, and configuration endpoints, their documents, and their problems.</summary>
public class InstallationConfigurationEndpointsTests
{
    private const string ProblemPrefix = "urn:hephaestus:problem:";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ControlApiClient ClientFor(RunningHost host, Language language = Language.English) => new(host.Settings.ControlSocketPath, language);

    private static string Alias(string? agent, string? model, string? effort) =>
        JsonSerializer.Serialize(new { agent, model, effort });

    [Fact]
    public async Task An_Agent_Alias_is_stored_answers_201_with_the_document_and_a_second_put_answers_200()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var first = await client.SendAsync(HttpMethod.Put, "/api/aliases/opus5.5", Alias("claude-code", "claude-opus-5-5", null), Token);
        var second = await client.SendAsync(HttpMethod.Put, "/api/aliases/opus5.5", Alias("claude-code", "claude-opus-5-5", null), Token);

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Matches("""^\{"name":"opus5\.5","agent":"claude-code","model":"claude-opus-5-5","effort":null,"addedAt":"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ"\}$""", first.Body);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(first.Body, second.Body);
    }

    [Fact]
    public async Task An_Effort_Alias_is_a_document_with_the_Agent_and_the_Model_null()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/aliases/deep", Alias(null, null, "xhigh"), Token);

        Assert.Equal(HttpStatusCode.Created, answer.Status);
        Assert.Matches("""^\{"name":"deep","agent":null,"model":null,"effort":"xhigh","addedAt":"[^"]+"\}$""", answer.Body);
    }

    [Theory]
    [InlineData("opus", "gemini", "m", null, HttpStatusCode.UnprocessableEntity, "alias-agent-unknown", "Unknown Agent")]
    [InlineData("opus", null, null, "ultra", HttpStatusCode.UnprocessableEntity, "alias-effort-unknown", "Unknown Effort")]
    [InlineData("opus", "claude-code", null, null, HttpStatusCode.BadRequest, "alias-incomplete", "An Alias names an Agent and a Model, or an Effort")]
    [InlineData("opus", "claude-code", "-m", null, HttpStatusCode.BadRequest, "alias-model-invalid", "Invalid Model")]
    [InlineData("Opus", "claude-code", "m", null, HttpStatusCode.BadRequest, "alias-name-invalid", "Invalid Alias name")]
    public async Task An_Alias_Core_refuses_is_a_problem_and_nothing_is_stored(string name, string? agent, string? model, string? effort, HttpStatusCode status, string slug, string title)
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, $"/api/aliases/{name}", Alias(agent, model, effort), Token);

        Assert.Equal(status, answer.Status);
        Assert.Equal(ProblemPrefix + slug, answer.Problem!.Type);
        Assert.Equal(title, answer.Problem.Title);
        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListAliasesAsync(Token));
    }

    [Fact]
    public async Task The_refusal_of_an_Agent_names_the_value_and_the_two_Agents()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/aliases/x", Alias("gemini", "m", null), Token);

        Assert.Contains("'gemini'", answer.Problem!.Detail, StringComparison.Ordinal);
        Assert.Contains("claude-code, codex-cli", answer.Problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_Alias_that_means_something_else_is_a_conflict_and_stays_as_it_was()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);
        await client.SendAsync(HttpMethod.Put, "/api/aliases/opus", Alias("claude-code", "claude-opus-5-5", null), Token);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/aliases/opus", Alias("codex-cli", "gpt-5.5", null), Token);

        Assert.Equal(HttpStatusCode.Conflict, answer.Status);
        Assert.Equal(ProblemPrefix + "alias-defined-differently", answer.Problem!.Type);
        Assert.Equal("claude-opus-5-5", (await host.App.Services.GetRequiredService<IReadStore>().ListAliasesAsync(Token)).Single().Model);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task A_body_that_is_not_an_Alias_document_is_a_request_problem(string body)
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/aliases/opus", body, Token);

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal(ProblemPrefix + "request-invalid", answer.Problem!.Type);
    }

    [Fact]
    public async Task A_body_longer_than_a_document_may_be_is_refused_without_being_read_whole()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/aliases/opus", Alias("claude-code", new string('m', 20_000), null), Token);

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal(ProblemPrefix + "request-invalid", answer.Problem!.Type);
    }

    [Fact]
    public async Task Aliases_are_listed_by_name_and_removing_one_answers_it_and_a_second_removal_is_a_404()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);
        await client.SendAsync(HttpMethod.Put, "/api/aliases/zeta", Alias(null, null, "low"), Token);
        await client.SendAsync(HttpMethod.Put, "/api/aliases/alpha", Alias("codex-cli", "gpt-5.5", null), Token);

        var list = await client.GetAsync("/api/aliases", Token);
        var removed = await client.SendAsync(HttpMethod.Delete, "/api/aliases/alpha", body: null, Token);
        var again = await client.SendAsync(HttpMethod.Delete, "/api/aliases/alpha", body: null, Token);

        Assert.Equal(["alpha", "zeta"], Names(list.Body));
        Assert.Equal(HttpStatusCode.OK, removed.Status);
        Assert.Contains("\"name\":\"alpha\"", removed.Body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, again.Status);
        Assert.Equal(ProblemPrefix + "alias-not-found", again.Problem!.Type);
        Assert.Equal("""{"aliases":[]}""", (await RemoveAllAndListAsync(client)).Body);
    }

    private static async Task<ApiAnswer> RemoveAllAndListAsync(ControlApiClient client)
    {
        await client.SendAsync(HttpMethod.Delete, "/api/aliases/zeta", body: null, Token);
        return await client.GetAsync("/api/aliases", Token);
    }

    private static string[] Names(string body) =>
        [.. JsonDocument.Parse(body).RootElement.GetProperty("aliases").EnumerateArray().Select(a => a.GetProperty("name").GetString()!)];

    [Fact]
    public async Task A_new_installation_has_no_limits_and_they_are_null_not_zero()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.GetAsync("/api/limits", Token);

        Assert.Equal("""{"taskDuration":null,"cpus":null,"memory":null}""", answer.Body);
    }

    [Fact]
    public async Task Limits_are_set_in_their_written_form_and_a_change_keeps_the_others()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var first = await client.SendAsync(HttpMethod.Put, "/api/limits", """{"taskDuration":"90m","cpus":"2.50","memory":"4096m"}""", Token);
        var second = await client.SendAsync(HttpMethod.Put, "/api/limits", """{"memory":"8g"}""", Token);
        var read = await client.GetAsync("/api/limits", Token);

        Assert.Equal("""{"taskDuration":"1h30m","cpus":"2.5","memory":"4g"}""", first.Body);
        Assert.Equal("""{"taskDuration":"1h30m","cpus":"2.5","memory":"8g"}""", second.Body);
        Assert.Equal(second.Body, read.Body);
    }

    [Theory]
    [InlineData("""{"taskDuration":"soon"}""", HttpStatusCode.UnprocessableEntity, "limit-task-duration-invalid")]
    [InlineData("""{"cpus":"lots"}""", HttpStatusCode.UnprocessableEntity, "limit-cpus-invalid")]
    [InlineData("""{"memory":"plenty"}""", HttpStatusCode.UnprocessableEntity, "limit-memory-invalid")]
    [InlineData("{}", HttpStatusCode.BadRequest, "limit-nothing-given")]
    [InlineData("not json", HttpStatusCode.BadRequest, "request-invalid")]
    public async Task A_limit_out_of_shape_or_no_limit_is_a_problem_and_nothing_changes(string body, HttpStatusCode status, string slug)
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/limits", body, Token);

        Assert.Equal(status, answer.Status);
        Assert.Equal(ProblemPrefix + slug, answer.Problem!.Type);
        Assert.Equal("""{"taskDuration":null,"cpus":null,"memory":null}""", (await client.GetAsync("/api/limits", Token)).Body);
    }

    [Fact]
    public async Task A_new_installation_lists_the_two_default_protected_paths()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.GetAsync("/api/protected-paths", Token);

        Assert.Equal("""{"protectedPaths":[".github/**","hephaestus.yml"]}""", answer.Body);
    }

    [Fact]
    public async Task A_protected_path_travels_in_the_query_so_slashes_and_special_characters_arrive_as_typed()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);
        const string glob = "docs/a&b#c?d=e/**";
        var path = "/api/protected-paths?pattern=" + Uri.EscapeDataString(glob);

        var first = await client.SendAsync(HttpMethod.Put, path, body: null, Token);
        var second = await client.SendAsync(HttpMethod.Put, path, body: null, Token);
        var removed = await client.SendAsync(HttpMethod.Delete, path, body: null, Token);

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal(JsonSerializer.Serialize(new { pattern = glob }), first.Body);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(HttpStatusCode.OK, removed.Status);
        Assert.DoesNotContain(glob, await host.App.Services.GetRequiredService<IReadStore>().ListProtectedPathsAsync(Token));
    }

    [Theory]
    [InlineData("/api/protected-paths?pattern=..%2Fx", HttpStatusCode.BadRequest, "protected-path-invalid")]
    [InlineData("/api/protected-paths?pattern=%2Fetc", HttpStatusCode.BadRequest, "protected-path-invalid")]
    [InlineData("/api/protected-paths", HttpStatusCode.BadRequest, "protected-path-invalid")]
    public async Task A_glob_out_of_shape_or_missing_is_refused_on_add(string path, HttpStatusCode status, string slug)
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, path, body: null, Token);

        Assert.Equal(status, answer.Status);
        Assert.Equal(ProblemPrefix + slug, answer.Problem!.Type);
        Assert.Equal(2, (await host.App.Services.GetRequiredService<IReadStore>().ListProtectedPathsAsync(Token)).Count);
    }

    [Theory]
    [InlineData("/api/protected-paths?pattern=docs%2F**")]
    [InlineData("/api/protected-paths?pattern=..%2Fx")]
    [InlineData("/api/protected-paths")]
    public async Task Removing_a_glob_that_is_not_listed_is_a_404(string path)
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Delete, path, body: null, Token);

        Assert.Equal(HttpStatusCode.NotFound, answer.Status);
        Assert.Equal(ProblemPrefix + "protected-path-not-found", answer.Problem!.Type);
    }

    [Fact]
    public async Task A_new_installation_is_in_English_and_a_setting_is_stored_by_name_with_its_default()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var before = await client.GetAsync("/api/settings", Token);
        var set = await client.SendAsync(HttpMethod.Put, "/api/settings/language", "pl", Token);
        var after = await client.GetAsync("/api/settings", Token);

        Assert.Equal("""{"settings":[{"name":"language","value":"en","default":"en","isSet":false},{"name":"pollingInterval","value":"60","default":"60","isSet":false}]}""", before.Body);
        Assert.Equal("""{"name":"language","value":"pl","default":"en","isSet":true}""", set.Body);
        Assert.Equal("""{"settings":[{"name":"language","value":"pl","default":"en","isSet":true},{"name":"pollingInterval","value":"60","default":"60","isSet":false}]}""", after.Body);
    }

    [Theory]
    [InlineData("language", "tlh", HttpStatusCode.UnprocessableEntity, "setting-value-invalid")]
    [InlineData("language", "", HttpStatusCode.UnprocessableEntity, "setting-value-invalid")]
    [InlineData("colour", "blue", HttpStatusCode.NotFound, "setting-unknown")]
    public async Task A_value_the_setting_does_not_accept_and_a_name_that_is_no_setting_are_problems(string name, string value, HttpStatusCode status, string slug)
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, $"/api/settings/{name}", value, Token);

        Assert.Equal(status, answer.Status);
        Assert.Equal(ProblemPrefix + slug, answer.Problem!.Type);
        Assert.Contains("\"value\":\"en\"", (await client.GetAsync("/api/settings", Token)).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_value_a_setting_refuses_is_named_with_what_it_accepts()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Put, "/api/settings/language", "tlh", Token);

        Assert.Contains("'tlh'", answer.Problem!.Detail, StringComparison.Ordinal);
        Assert.Contains("en, pl", answer.Problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_problem_without_a_language_in_the_request_is_in_the_installations_stored_language()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);
        await client.SendAsync(HttpMethod.Put, "/api/settings/language", "pl", Token);

        var english = await RawGetAsync(host.Settings.ControlSocketPath, "/api/nope", acceptLanguage: "en");
        var stored = await RawGetAsync(host.Settings.ControlSocketPath, "/api/nope", acceptLanguage: null);

        Assert.Contains("Not found", english, StringComparison.Ordinal);
        Assert.Contains("Nie znaleziono", stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_stored_language_a_problem_is_in_the_language_the_environment_names()
    {
        await using var host = await RunningHost.StartAsync(Language.Polish);

        var body = await RawGetAsync(host.Settings.ControlSocketPath, "/api/nope", acceptLanguage: null);

        Assert.Contains("Nie znaleziono", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_problem_is_still_told_in_the_environments_language_when_the_stored_one_cannot_be_read()
    {
        using var installation = new TempSocket();
        await using var host = await RunningHost.StartAsync(Language.Polish, installation: installation);
        Directory.Delete(installation.DataDirectory, recursive: true);

        var body = await RawGetAsync(host.Settings.ControlSocketPath, "/api/nope", acceptLanguage: null);

        Assert.Contains("Nie znaleziono", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_whole_configuration_is_one_document_with_secrets_by_name_only()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);
        const string secretValue = "sk-test-0123456789-abcdefghij";
        await client.SendAsync(HttpMethod.Put, "/api/secrets/anthropic", secretValue, Token);
        await client.SendAsync(HttpMethod.Put, "/api/aliases/opus", Alias("claude-code", "claude-opus-5-5", null), Token);
        await client.SendAsync(HttpMethod.Put, "/api/limits", """{"taskDuration":"2h","cpus":"2","memory":"4g"}""", Token);
        await client.SendAsync(HttpMethod.Put, "/api/settings/language", "pl", Token);

        var answer = await client.GetAsync("/api/configuration", Token);

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.DoesNotContain(secretValue, answer.Body, StringComparison.Ordinal);
        var root = JsonDocument.Parse(answer.Body).RootElement;
        Assert.Equal(["trustedUsers", "aliases", "limits", "protectedPaths", "settings", "secrets"], root.EnumerateObject().Select(p => p.Name));
        Assert.Empty(root.GetProperty("trustedUsers").EnumerateArray());
        Assert.Equal("opus", root.GetProperty("aliases")[0].GetProperty("name").GetString());
        Assert.Equal("2h", root.GetProperty("limits").GetProperty("taskDuration").GetString());
        Assert.Equal([".github/**", "hephaestus.yml"], root.GetProperty("protectedPaths").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("pl", root.GetProperty("settings")[0].GetProperty("value").GetString());
        Assert.Equal("anthropic", root.GetProperty("secrets")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Every_change_is_on_the_control_socket_and_other_methods_are_refused()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = ClientFor(host);

        var post = await client.SendAsync(HttpMethod.Post, "/api/aliases/opus", Alias(null, null, "low"), Token);
        var patch = await client.SendAsync(HttpMethod.Patch, "/api/limits", "{}", Token);
        var configuration = await client.SendAsync(HttpMethod.Put, "/api/configuration", "{}", Token);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.Status);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, patch.Status);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, configuration.Status);
    }

    private static async Task<string> RawGetAsync(string socketPath, string path, string? acceptLanguage)
    {
        using var http = new HttpClient(new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            },
        })
        {
            BaseAddress = new Uri("http://hephaestus.control/"),
        };
        if (acceptLanguage is not null)
        {
            http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(acceptLanguage));
        }

        using var response = await http.GetAsync(path.TrimStart('/'), Token);
        return await response.Content.ReadAsStringAsync(Token);
    }
}
