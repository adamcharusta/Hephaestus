using Hephaestus.Core.Localization;
using Hephaestus.Core.Loop;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Host.Tests.Cli;

/// <summary>Story 2.2, AR-42: the API reports that the loop is alive and when its last tick ran, and the <c>health</c> command prints it.</summary>
public class HealthCommandTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task WaitForTickAsync(RunningHost host)
    {
        var heartbeat = host.App.Services.GetRequiredService<LoopHeartbeat>();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (heartbeat.Check().LastTickAt is null && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10, Token);
        }
    }

    [Fact]
    public async Task A_running_host_says_its_loop_is_alive_and_when_it_last_ticked()
    {
        await using var host = await RunningHost.StartAsync();
        await WaitForTickAsync(host);

        var run = await CliRun.ExecuteAsync(host.Settings.ControlSocketPath, Language.English, "health");

        Assert.Equal(0, run.ExitCode);
        Assert.Matches(@"^Loop: alive, last tick \d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z\.\r?\n$", run.Output);
        Assert.Equal(string.Empty, run.Error);
    }

    [Fact]
    public async Task Under_json_the_command_prints_the_API_document_and_nothing_else()
    {
        await using var host = await RunningHost.StartAsync();
        await WaitForTickAsync(host);

        var run = await CliRun.ExecuteAsync(host.Settings.ControlSocketPath, Language.English, "health", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Matches(@"^\{""loop"":\{""alive"":true,""lastTickAt"":""\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z""\}\}\r?\n$", run.Output);
        Assert.Equal(string.Empty, run.Error);
    }

    [Fact]
    public async Task In_Polish_only_the_sentence_differs()
    {
        await using var host = await RunningHost.StartAsync();
        await WaitForTickAsync(host);
        var socket = host.Settings.ControlSocketPath;

        var polish = await CliRun.ExecuteAsync(socket, Language.Polish, "health");
        var english = await CliRun.ExecuteAsync(socket, Language.English, "health", "--json");
        var polishJson = await CliRun.ExecuteAsync(socket, Language.Polish, "health", "--json");

        Assert.StartsWith("Pętla: działa, ostatni takt ", polish.Output, StringComparison.Ordinal);
        Assert.Equal(0, polish.ExitCode);
        Assert.Equal(english.Output.Length, polishJson.Output.Length);
    }

    [Fact]
    public async Task The_endpoint_is_a_camel_case_document_on_the_control_socket()
    {
        await using var host = await RunningHost.StartAsync();
        await WaitForTickAsync(host);
        using var client = new Hephaestus.Host.Cli.ControlApiClient(host.Settings.ControlSocketPath, Language.English);

        var answer = await client.GetAsync("/api/health", Token);

        Assert.Equal(System.Net.HttpStatusCode.OK, answer.Status);
        Assert.StartsWith("""{"loop":{"alive":true,"lastTickAt":"20""", answer.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Language.English, """{"loop":{"alive":false,"lastTickAt":"2026-11-27T12:00:00Z"}}""", "Loop: not alive, last tick 2026-11-27T12:00:00Z. Read the container log with 'docker compose logs hephaestus'.")]
    [InlineData(Language.English, """{"loop":{"alive":false,"lastTickAt":null}}""", "Loop: not alive, it has not ticked since the host started. Read the container log with 'docker compose logs hephaestus'.")]
    [InlineData(Language.Polish, """{"loop":{"alive":false,"lastTickAt":"2026-11-27T12:00:00Z"}}""", "Pętla: nie działa, ostatni takt 2026-11-27T12:00:00Z. Sprawdź log kontenera poleceniem 'docker compose logs hephaestus'.")]
    public async Task A_loop_that_is_not_alive_is_said_so_with_where_to_look_and_the_exit_code_is_1(Language language, string document, string sentence)
    {
        await using var api = await StubApi.StartAsync(app => app.MapGet("/api/health", () => Results.Text(document, "application/json")));

        var run = await CliRun.ExecuteAsync(api.SocketPath, language, "health");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(sentence + Environment.NewLine, run.Output);
        Assert.Equal(string.Empty, run.Error);
    }

    [Fact]
    public async Task A_loop_that_is_not_alive_is_the_exit_code_1_under_json_too_and_the_document_is_still_printed()
    {
        await using var api = await StubApi.StartAsync(app => app.MapGet("/api/health", () => Results.Text("""{"loop":{"alive":false,"lastTickAt":null}}""", "application/json")));

        var run = await CliRun.ExecuteAsync(api.SocketPath, Language.English, "health", "--json");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("""{"loop":{"alive":false,"lastTickAt":null}}""" + Environment.NewLine, run.Output);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task An_answer_that_is_not_the_health_document_is_a_failure_not_a_crash(string body)
    {
        await using var api = await StubApi.StartAsync(app => app.MapGet("/api/health", () => Results.Text(body, "application/json")));

        var run = await CliRun.ExecuteAsync(api.SocketPath, Language.English, "health");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Equal("Unexpected answer", run.ErrorLines[0]);
    }

    [Fact]
    public async Task A_problem_from_the_API_is_printed_as_its_title_then_its_detail()
    {
        await using var api = await StubApi.StartAsync(app => app.MapGet("/api/health", (HttpContext context) =>
        {
            context.Response.StatusCode = 503;
            context.Response.ContentType = "application/problem+json";
            return context.Response.WriteAsync("""{"type":"urn:test","title":"Busy","status":503,"detail":"Try later."}""");
        }));

        var run = await CliRun.ExecuteAsync(api.SocketPath, Language.English, "health");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(["Busy", "Try later."], run.ErrorLines);
    }

    [Fact]
    public async Task When_nothing_listens_the_command_fails_like_every_other()
    {
        using var socket = new TempSocket();

        var run = await CliRun.ExecuteAsync(socket.Path, Language.English, "health");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("Cannot reach Hephaestus", run.ErrorLines[0]);
    }

    [Fact]
    public async Task The_command_is_listed_in_the_help_with_its_own_description()
    {
        using var socket = new TempSocket();

        var root = await CliRun.ExecuteAsync(socket.Path, Language.English);
        var own = await CliRun.ExecuteAsync(socket.Path, Language.English, "health", "--help");

        Assert.Contains("health", root.Output, StringComparison.Ordinal);
        Assert.Contains("Reports whether the orchestrator loop is alive and when it last ticked.", own.Output, StringComparison.Ordinal);
    }
}
