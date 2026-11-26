using Hephaestus.Core;
using Hephaestus.Core.Localization;

namespace Hephaestus.Host.Tests.Cli;

/// <summary>The I/O matrix of Story 1.4 for the version command, the first command of the command line.</summary>
public class VersionCommandTests
{
    [Fact]
    public async Task Version_prints_one_sentence_with_the_version_and_exits_0()
    {
        await using var host = await RunningHost.StartAsync();

        var run = await CliRun.ExecuteAsync(host.Settings.ControlSocketPath, Language.English, "version");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal($"Hephaestus version {HephaestusVersion.Current}{Environment.NewLine}", run.Output);
        Assert.Equal(string.Empty, run.Error);
    }

    [Fact]
    public async Task Version_json_prints_the_api_document_and_nothing_else()
    {
        await using var host = await RunningHost.StartAsync();

        var run = await CliRun.ExecuteAsync(host.Settings.ControlSocketPath, Language.English, "version", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal($$"""{"version":"{{HephaestusVersion.Current}}"}{{Environment.NewLine}}""", run.Output);
        Assert.Equal(string.Empty, run.Error);
    }

    [Fact]
    public async Task In_Polish_only_the_sentence_differs_the_command_the_exit_code_and_the_json_document_do_not()
    {
        await using var host = await RunningHost.StartAsync();
        var socket = host.Settings.ControlSocketPath;

        var english = await CliRun.ExecuteAsync(socket, Language.English, "version");
        var polish = await CliRun.ExecuteAsync(socket, Language.Polish, "version");
        var englishJson = await CliRun.ExecuteAsync(socket, Language.English, "version", "--json");
        var polishJson = await CliRun.ExecuteAsync(socket, Language.Polish, "version", "--json");

        Assert.NotEqual(english.Output, polish.Output);
        Assert.Equal($"Hephaestus w wersji {HephaestusVersion.Current}{Environment.NewLine}", polish.Output);
        Assert.Equal(english.ExitCode, polish.ExitCode);
        Assert.Equal(englishJson, polishJson);
    }

    [Fact]
    public async Task The_language_comes_from_the_environment_and_an_unknown_value_is_English()
    {
        await using var host = await RunningHost.StartAsync();
        var environment = new Dictionary<string, string?>
        {
            [HostSettings.ControlSocketVariable] = host.Settings.ControlSocketPath,
            [HostSettings.LanguageVariable] = "tlh",
        };

        using var output = new StringWriter();
        var exitCode = await EntryPoint.RunAsync(["version"], TextReader.Null, output, TextWriter.Null, name => environment.GetValueOrDefault(name), TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.StartsWith("Hephaestus version", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Language.English, "Cannot reach Hephaestus")]
    [InlineData(Language.Polish, "Nie można połączyć się z Hephaestusem")]
    public async Task When_nothing_listens_the_command_fails_with_a_title_then_a_detail_on_standard_error(Language language, string title)
    {
        using var socket = new TempSocket();

        var run = await CliRun.ExecuteAsync(socket.Path, language, "version");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Equal(2, run.ErrorLines.Length);
        Assert.Equal(title, run.ErrorLines[0]);
        Assert.Contains(socket.Path, run.ErrorLines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_problem_document_from_the_api_is_printed_as_its_title_then_its_detail_and_the_exit_code_is_1()
    {
        await using var api = await StubApi.StartAsync(app => app.MapGet("/api/version", (HttpContext context) =>
        {
            context.Response.StatusCode = 503;
            context.Response.ContentType = "application/problem+json";
            return context.Response.WriteAsync("""{"type":"urn:test","title":"Busy","status":503,"detail":"Try later."}""");
        }));

        var run = await CliRun.ExecuteAsync(api.SocketPath, Language.English, "version", "--json");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Equal(["Busy", "Try later."], run.ErrorLines);
    }

    [Theory]
    [InlineData("text/plain", "boom")]
    [InlineData("application/problem+json", "not json")]
    [InlineData("application/problem+json", """{"type":"urn:test","status":500}""")]
    public async Task An_error_that_is_not_a_problem_document_gets_a_generic_message_and_the_exit_code_is_1(string contentType, string body)
    {
        await using var api = await StubApi.StartAsync(app => app.MapGet("/api/version", (HttpContext context) =>
        {
            context.Response.StatusCode = 502;
            context.Response.ContentType = contentType;
            return context.Response.WriteAsync(body);
        }));

        var run = await CliRun.ExecuteAsync(api.SocketPath, Language.English, "version");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Equal(["Unexpected answer", "The API failed with status 502 and did not describe the failure."], run.ErrorLines);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task A_success_that_is_not_the_version_document_is_a_failure_with_the_exit_code_1_not_a_crash(string body)
    {
        await using var api = await StubApi.StartAsync(app => app.MapGet("/api/version", () => Results.Text(body, "application/json")));

        var run = await CliRun.ExecuteAsync(api.SocketPath, Language.English, "version");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Equal("Unexpected answer", run.ErrorLines[0]);
    }

    [Fact]
    public async Task A_cancelled_run_is_not_reported_as_an_unreachable_host()
    {
        using var socket = new TempSocket();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using var client = new Hephaestus.Host.Cli.ControlApiClient(socket.Path, Language.English);
        var application = new Hephaestus.Host.Cli.CliApplication(socket.Settings(), client, TextReader.Null, TextWriter.Null, TextWriter.Null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => application.RunAsync(["version"], cancelled.Token));
    }

    [Fact]
    public async Task The_client_adds_nothing_to_the_api_document_under_json()
    {
        await using var api = await StubApi.StartAsync(app => app.MapGet("/api/version", () => Results.Text("""{"version":"9.9.9","extra":1}""", "application/json")));

        var run = await CliRun.ExecuteAsync(api.SocketPath, Language.Polish, "version", "--json");

        Assert.Equal($$"""{"version":"9.9.9","extra":1}{{Environment.NewLine}}""", run.Output);
    }
}
