using System.Net;
using System.Text.Json;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Workspaces;
using Hephaestus.Host.Cli;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Hephaestus.Host.Tests.Api;

/// <summary>Story 3.10 on the control API: how an Agent signs in, the credential in the body of the request and nowhere in an answer, and the probe that says whether the Agent is ready.</summary>
public class AgentEndpointsTests
{
    private const string ProblemPrefix = "urn:hephaestus:problem:";
    private const string Credential = "sk-ant-api03-0123456789-the-key";
    private const string Ready = """{"type":"result","subtype":"success","is_error":false,"result":"pong"}""";
    private const string Failed = """{"type":"result","subtype":"error_during_execution","is_error":true}""";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ControlApiClient ClientFor(RunningHost host, Language language = Language.English) => new(host.Settings.ControlSocketPath, language);

    /// <summary>A host whose runtime answers every probe with what the test says, and records what it was asked.</summary>
    internal static async Task<(RunningHost Host, IWorkspaceRuntime Runtime)> StartAsync(Func<ProbeRequest, ProbeResult>? probe = null)
    {
        var runtime = Substitute.For<IWorkspaceRuntime>();
        runtime.RunProbeAsync(Arg.Any<ProbeRequest>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult((probe ?? (_ => new ProbeResult(0, StepExitCause.Exited, [Ready])))(call.Arg<ProbeRequest>())));
        var host = await RunningHost.StartAsync(configure: builder => builder.Services.AddSingleton(runtime));
        return (host, runtime);
    }

    private static Task<ApiAnswer> SetAsync(ControlApiClient client, string agent, string? mode, string credential) =>
        client.SendAsync(HttpMethod.Put, $"/api/agents/{agent}/authentication" + (mode is null ? string.Empty : $"?mode={mode}"), credential, Token);

    [Fact]
    public async Task A_new_installation_lists_both_Agents_with_nothing_chosen()
    {
        var (host, _) = await StartAsync();
        await using var _1 = host;
        using var client = ClientFor(host);

        var answer = await client.GetAsync("/api/agents", Token);

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Equal("""{"agents":[{"agent":"claude-code","mode":null,"chosenAt":null,"secret":null,"credentialStored":null,"status":null,"checkedAt":null,"reason":null},{"agent":"codex-cli","mode":null,"chosenAt":null,"secret":null,"credentialStored":null,"status":null,"checkedAt":null,"reason":null}]}""", answer.Body);
    }

    [Theory]
    [InlineData("api-key", "anthropic-api-key")]
    [InlineData("subscription", "claude-code-oauth-token")]
    public async Task Choosing_a_mode_stores_the_credential_of_the_body_as_the_secret_of_that_mode_and_answers_the_document_without_it(string mode, string secret)
    {
        var (host, _) = await StartAsync();
        await using var _1 = host;
        using var client = ClientFor(host);

        var answer = await SetAsync(client, "claude-code", mode, Credential);

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        using var document = JsonDocument.Parse(answer.Body);
        Assert.Equal(mode, document.RootElement.GetProperty("mode").GetString());
        Assert.Equal(secret, document.RootElement.GetProperty("secret").GetString());
        Assert.True(document.RootElement.GetProperty("credentialStored").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("status").ValueKind);
        Assert.DoesNotContain(Credential, answer.Body, StringComparison.Ordinal);
        Assert.Equal(Credential, await File.ReadAllTextAsync(Path.Combine(host.Settings.SecretsDirectory, secret), Token));
        Assert.Contains(secret, await host.App.Services.GetRequiredService<IReadStore>().ListSecretNamesAsync(Token));
    }

    [Fact]
    public async Task The_mode_that_was_chosen_is_listed_and_read_back_by_the_next_request()
    {
        var (host, _) = await StartAsync();
        await using var _1 = host;
        using var client = ClientFor(host);
        await SetAsync(client, "claude-code", "subscription", Credential);

        var list = await client.GetAsync("/api/agents", Token);

        using var document = JsonDocument.Parse(list.Body);
        var claude = document.RootElement.GetProperty("agents")[0];
        Assert.Equal("subscription", claude.GetProperty("mode").GetString());
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", claude.GetProperty("chosenAt").GetString());
        Assert.DoesNotContain(Credential, list.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gemini", "api-key", HttpStatusCode.UnprocessableEntity, "agent-unknown", "The Agent is not known")]
    [InlineData("claude-code", "oauth", HttpStatusCode.UnprocessableEntity, "agent-mode-unknown", "The authentication mode is not known")]
    [InlineData("claude-code", null, HttpStatusCode.UnprocessableEntity, "agent-mode-unknown", "The authentication mode is not known")]
    [InlineData("codex-cli", "api-key", HttpStatusCode.UnprocessableEntity, "agent-no-adapter", "This Agent cannot be set up yet")]
    public async Task An_Agent_or_a_mode_that_cannot_be_chosen_is_a_problem_and_nothing_is_stored(string agent, string? mode, HttpStatusCode status, string slug, string title)
    {
        var (host, _) = await StartAsync();
        await using var _1 = host;
        using var client = ClientFor(host);

        var answer = await SetAsync(client, agent, mode, Credential);

        Assert.Equal(status, answer.Status);
        Assert.Equal(ProblemPrefix + slug, answer.Problem!.Type);
        Assert.Equal(title, answer.Problem.Title);
        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListSecretNamesAsync(Token));
        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListAgentSettingsAsync(Token));
    }

    [Fact]
    public async Task The_problem_of_an_Agent_that_is_not_known_names_the_value_and_the_two_Agents_and_that_of_a_mode_names_the_two_modes()
    {
        var (host, _) = await StartAsync();
        await using var _1 = host;
        using var client = ClientFor(host);

        var agent = await SetAsync(client, "gemini", "api-key", Credential);
        var mode = await SetAsync(client, "claude-code", "oauth", Credential);

        Assert.Contains("'gemini'", agent.Problem!.Detail, StringComparison.Ordinal);
        Assert.Contains("claude-code, codex-cli", agent.Problem.Detail, StringComparison.Ordinal);
        Assert.Contains("'oauth'", mode.Problem!.Detail, StringComparison.Ordinal);
        Assert.Contains("api-key, subscription", mode.Problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_credential_that_is_too_short_is_the_problem_of_a_secret_value_and_nothing_is_stored()
    {
        var (host, _) = await StartAsync();
        await using var _1 = host;
        using var client = ClientFor(host);

        var answer = await SetAsync(client, "claude-code", "api-key", "short");

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal(ProblemPrefix + "secret-value-too-short", answer.Problem!.Type);
        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListSecretNamesAsync(Token));
    }

    [Fact]
    public async Task An_empty_body_keeps_the_credential_that_is_stored_and_is_refused_when_none_is()
    {
        var (host, _) = await StartAsync();
        await using var _1 = host;
        using var client = ClientFor(host);

        var refused = await SetAsync(client, "claude-code", "api-key", string.Empty);
        await client.SendAsync(HttpMethod.Put, "/api/secrets/anthropic-api-key", Credential, Token);
        var kept = await SetAsync(client, "claude-code", "api-key", string.Empty);

        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Equal(ProblemPrefix + "agent-credential-required", refused.Problem!.Type);
        Assert.Contains("anthropic-api-key", refused.Problem.Detail, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, kept.Status);
        Assert.Equal(Credential, await File.ReadAllTextAsync(Path.Combine(host.Settings.SecretsDirectory, "anthropic-api-key"), Token));
    }

    [Fact]
    public async Task A_check_runs_the_probe_and_reports_the_Agent_ready_with_the_time_of_the_probe()
    {
        var (host, runtime) = await StartAsync();
        await using var _1 = host;
        using var client = ClientFor(host);
        await SetAsync(client, "claude-code", "subscription", Credential);

        var answer = await client.SendAsync(HttpMethod.Post, "/api/agents/claude-code/check", null, Token);

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        using var document = JsonDocument.Parse(answer.Body);
        Assert.Equal("ready", document.RootElement.GetProperty("status").GetString());
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", document.RootElement.GetProperty("checkedAt").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("reason").ValueKind);
        var probe = Assert.Single(runtime.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<ProbeRequest>());
        Assert.Equal(["CLAUDE_CODE_OAUTH_TOKEN"], probe.Invocation.SecretReferences.Select(reference => reference.Target));
        Assert.Equal(["api.anthropic.com"], probe.Invocation.EgressHosts);
        Assert.DoesNotContain(Credential, string.Join(' ', probe.Invocation.Command), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_check_that_finds_the_Agent_unavailable_stores_it_with_its_reason_and_the_list_shows_it()
    {
        var (host, _) = await StartAsync(_ => new ProbeResult(1, StepExitCause.Exited, [Failed]));
        await using var _1 = host;
        using var client = ClientFor(host);
        await SetAsync(client, "claude-code", "api-key", Credential);

        var answer = await client.SendAsync(HttpMethod.Post, "/api/agents/claude-code/check", null, Token);
        var list = await client.GetAsync("/api/agents", Token);

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        using var document = JsonDocument.Parse(answer.Body);
        Assert.Equal("unavailable", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("agent.probe.error", document.RootElement.GetProperty("reason").GetProperty("id").GetString());
        Assert.Equal(["claude-code", "1"], document.RootElement.GetProperty("reason").GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()));
        using var listed = JsonDocument.Parse(list.Body);
        var claude = listed.RootElement.GetProperty("agents")[0];
        Assert.Equal("unavailable", claude.GetProperty("status").GetString());
        Assert.Equal(document.RootElement.GetProperty("checkedAt").GetString(), claude.GetProperty("checkedAt").GetString());
        Assert.Equal("agent.probe.error", claude.GetProperty("reason").GetProperty("id").GetString());
        var capacity = Assert.Single(await host.App.Services.GetRequiredService<IReadStore>().ListAgentCapacitiesAsync(Token));
        Assert.False(capacity.HasCapacity(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task A_check_of_a_credential_that_is_not_stored_finds_it_unavailable_without_running_a_probe()
    {
        var (host, runtime) = await StartAsync();
        await using var _1 = host;
        using var client = ClientFor(host);
        await SetAsync(client, "claude-code", "api-key", Credential);
        await client.SendAsync(HttpMethod.Delete, "/api/secrets/anthropic-api-key", null, Token);

        var answer = await client.SendAsync(HttpMethod.Post, "/api/agents/claude-code/check", null, Token);

        using var document = JsonDocument.Parse(answer.Body);
        Assert.Equal("unavailable", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("agent.probe.credential-missing", document.RootElement.GetProperty("reason").GetProperty("id").GetString());
        Assert.Empty(runtime.ReceivedCalls());
    }

    [Fact]
    public async Task A_check_of_an_Agent_with_no_chosen_mode_is_a_conflict_that_says_how_to_choose_one()
    {
        var (host, _) = await StartAsync();
        await using var _1 = host;
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Post, "/api/agents/claude-code/check", null, Token);

        Assert.Equal(HttpStatusCode.Conflict, answer.Status);
        Assert.Equal(ProblemPrefix + "agent-not-configured", answer.Problem!.Type);
        Assert.Contains("agent set-auth claude-code --mode api-key|subscription", answer.Problem.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gemini", HttpStatusCode.UnprocessableEntity, "agent-unknown")]
    [InlineData("codex-cli", HttpStatusCode.UnprocessableEntity, "agent-no-adapter")]
    public async Task A_check_of_an_Agent_that_is_not_known_or_has_no_adapter_is_a_problem(string agent, HttpStatusCode status, string slug)
    {
        var (host, _) = await StartAsync();
        await using var _1 = host;
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Post, $"/api/agents/{agent}/check", null, Token);

        Assert.Equal(status, answer.Status);
        Assert.Equal(ProblemPrefix + slug, answer.Problem!.Type);
    }

    [Fact]
    public async Task A_probe_that_cannot_run_is_a_503_with_the_reason_the_runtime_gave_and_nothing_is_recorded()
    {
        var (host, _) = await StartAsync(_ => throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceEgressProxyNotReady, "hephaestus:latest")));
        await using var _1 = host;
        using var client = ClientFor(host);
        await SetAsync(client, "claude-code", "api-key", Credential);

        var answer = await client.SendAsync(HttpMethod.Post, "/api/agents/claude-code/check", null, Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.Status);
        Assert.Equal(ProblemPrefix + "agent-probe-not-run", answer.Problem!.Type);
        Assert.Contains("could not run", answer.Problem.Detail, StringComparison.Ordinal);
        Assert.Contains("hephaestus:latest", answer.Problem.Detail, StringComparison.Ordinal);
        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListAgentCapacitiesAsync(Token));
    }

    [Fact]
    public async Task A_problem_is_written_in_the_language_of_the_request()
    {
        var (host, _) = await StartAsync();
        await using var _1 = host;
        using var polish = ClientFor(host, Language.Polish);

        var notKnown = await polish.SendAsync(HttpMethod.Post, "/api/agents/codex-cli/check", null, Token);

        Assert.Equal("Tego agenta nie można jeszcze skonfigurować", notKnown.Problem!.Title);
        Assert.Contains("codex-cli", notKnown.Problem.Detail, StringComparison.Ordinal);
    }
}
