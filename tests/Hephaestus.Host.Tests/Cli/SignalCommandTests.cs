using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.Host.Cli;

namespace Hephaestus.Host.Tests.Cli;

/// <summary>
/// Story 3.14, UX "Command Line", FR-16: <c>project start</c> and <c>project stop</c> print the Signal's id, wait up to ten seconds for the loop to apply it and say whether it was applied or rejected, and return at once with <c>--no-wait</c>;
/// <c>signal show</c> reads the status. A Signal that is still pending after the wait is reported as pending, with the command that follows it, and that is exit code 0.
/// </summary>
public class SignalCommandTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task<RunningHost> StartAsync(FakeCodeHost? gateway = null) =>
        RunningHost.StartAsync(configure: builder => builder.Services.AddSingleton<ICodeHost>(gateway ?? new FakeCodeHost()));

    private static Task<CliRun> RunAsync(RunningHost host, Language language, params string[] args) => CliRun.ExecuteAsync(host.Settings.ControlSocketPath, language, args);

    private static string Line(string text) => text + Environment.NewLine;

    /// <summary>A registered Project with a Task waiting in its Queue that cannot start (the fake GitHub has no file for it), so that a window opened on it stays open.</summary>
    private static async Task<RunningHost> HostWithAQueuedTaskAsync()
    {
        // The issue exists on GitHub, so that what shows the Task's state is made and the Task stays Queued.
        var gateway = new FakeCodeHost();
        gateway.IssueLabels[(101, 7)] = ["hephaestus"];
        var host = await StartAsync(gateway);
        var run = await RunAsync(host, Language.English, "project", "add", "acme/shop", "--base-branch", "main");
        Assert.Equal(0, run.ExitCode);
        var services = host.App.Services;
        await services.GetRequiredService<IInstallationConfiguration>().AddTrustedUserAsync(29, "alice", Token);
        await services.GetRequiredService<ISignalInbox>().AppendAsync([new Signal(Signal.GitHubSource, "issue-event:1", SignalKind.Labeled, 101, 7, 29, "alice", "hephaestus", null, null, DateTimeOffset.UtcNow, "Add a login page", "Use OAuth.")], Token);
        return host;
    }

    private static async Task<CliApplication> ApplicationAsync(StubApi api, StringWriter output, StringWriter error, TimeSpan wait)
    {
        var settings = new HostSettings(api.SocketPath, Language.English, "/tmp/none", "/tmp/none", "/tmp/none", "/tmp/none", "/tmp/none", HostSettings.DefaultInstallationId);
        await Task.CompletedTask;
        return new CliApplication(settings, new ControlApiClient(api.SocketPath, Language.English), new StringReader(string.Empty), output, error, signalWait: wait, signalPoll: TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public async Task Start_prints_the_Signal_id_waits_for_the_loop_and_says_that_a_window_is_open()
    {
        await using var host = await HostWithAQueuedTaskAsync();

        var run = await RunAsync(host, Language.English, "project", "start", "acme/shop");

        Assert.Equal(0, run.ExitCode);
        Assert.Matches(@"^Signal \d+ appended\.\r?\nA window is open on acme/shop\. Its Queue is worked until it is empty or you stop it\.\r?\n$", run.Output);
        Assert.Equal(string.Empty, run.Error);
        Assert.Single(await host.App.Services.GetRequiredService<IReadStore>().ListWindowsAsync(openOnly: true, Token));
    }

    [Fact]
    public async Task A_second_start_says_that_a_window_is_open_already_and_opens_none()
    {
        await using var host = await HostWithAQueuedTaskAsync();
        await RunAsync(host, Language.English, "project", "start", "acme/shop");

        var run = await RunAsync(host, Language.English, "project", "start", "acme/shop");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("A window is already open on acme/shop; none was opened.", run.Output, StringComparison.Ordinal);
        Assert.Single(await host.App.Services.GetRequiredService<IReadStore>().ListWindowsAsync(openOnly: false, Token));
    }

    [Fact]
    public async Task Stop_closes_the_window_and_a_stop_with_none_open_says_so()
    {
        await using var host = await HostWithAQueuedTaskAsync();
        await RunAsync(host, Language.English, "project", "start", "acme/shop");

        var stopped = await RunAsync(host, Language.English, "project", "stop", "acme/shop");
        var again = await RunAsync(host, Language.English, "project", "stop", "acme/shop");

        Assert.Contains("The window on acme/shop is closed. A Task that is running finishes; no other starts.", stopped.Output, StringComparison.Ordinal);
        Assert.Contains("No window is open on acme/shop; nothing was closed.", again.Output, StringComparison.Ordinal);
        Assert.Equal((0, 0), (stopped.ExitCode, again.ExitCode));
        Assert.Empty(await host.App.Services.GetRequiredService<IReadStore>().ListWindowsAsync(openOnly: true, Token));
    }

    [Fact]
    public async Task The_sentences_follow_the_language_and_the_command_names_do_not()
    {
        await using var host = await HostWithAQueuedTaskAsync();

        var run = await RunAsync(host, Language.Polish, "project", "start", "acme/shop");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Dodano sygnał ", run.Output, StringComparison.Ordinal);
        Assert.Contains("Okno dla acme/shop jest otwarte.", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_wait_returns_at_once_with_the_id_and_nothing_about_the_outcome()
    {
        await using var host = await HostWithAQueuedTaskAsync();

        var run = await RunAsync(host, Language.English, "project", "start", "acme/shop", "--no-wait");

        Assert.Equal(0, run.ExitCode);
        Assert.Matches(@"^Signal \d+ appended\.\r?\n$", run.Output);
    }

    [Fact]
    public async Task Json_prints_the_one_document_the_API_returns_and_nothing_else()
    {
        await using var host = await HostWithAQueuedTaskAsync();

        var pending = await RunAsync(host, Language.English, "project", "start", "acme/shop", "--no-wait", "--json");
        var settled = await RunAsync(host, Language.Polish, "project", "stop", "acme/shop", "--json");

        Assert.Matches("""^\{"id":\d+,"kind":"start-requested","status":"pending","reason":null\}\r?\n$""", pending.Output);
        Assert.Matches("""^\{"id":\d+,"kind":"stop-requested","status":"applied","reason":null\}\r?\n$""", settled.Output);
        Assert.Equal((0, 0), (pending.ExitCode, settled.ExitCode));
    }

    [Fact]
    public async Task A_Project_that_is_not_registered_is_a_failure_with_the_problem_on_the_error_stream()
    {
        await using var host = await StartAsync();

        var run = await RunAsync(host, Language.English, "project", "start", "acme/shop");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Equal(["Project not found", "acme/shop is not a registered Project. Nothing was changed. Run project list to see which are."], run.ErrorLines);
    }

    [Fact]
    public async Task Show_prints_the_status_of_a_Signal_as_lines_and_as_the_document()
    {
        await using var host = await HostWithAQueuedTaskAsync();
        var id = await host.App.Services.GetRequiredService<ISignalInbox>().AppendOneAsync(Signal.Start(999, Guid.NewGuid(), DateTimeOffset.UtcNow), Token);
        await Task.Delay(2500, Token);

        var lines = await RunAsync(host, Language.Polish, "signal", "show", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var json = await RunAsync(host, Language.English, "signal", "show", id.ToString(System.Globalization.CultureInfo.InvariantCulture), "--json");

        Assert.Equal(Line($"id: {id}") + Line("kind: start-requested") + Line("status: rejected") + Line("reason: project-not-registered"), lines.Output);
        Assert.Equal(Line($$"""{"id":{{id}},"kind":"start-requested","status":"rejected","reason":"project-not-registered"}"""), json.Output);
    }

    [Fact]
    public async Task Show_of_a_Signal_there_is_not_is_a_failure()
    {
        await using var host = await StartAsync();

        var run = await RunAsync(host, Language.English, "signal", "show", "12345");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(["Signal not found", "There is no Signal 12345."], run.ErrorLines);
    }

    [Fact]
    public async Task A_Signal_still_pending_after_the_wait_is_reported_as_pending_with_the_command_that_follows_it_and_is_not_a_failure()
    {
        await using var api = await StubApi.StartAsync(app =>
        {
            app.MapPost("/api/projects/start", () => Results.Text("""{"id":41,"kind":"start-requested","status":"pending","reason":null}""", "application/json", statusCode: 202));
            app.MapGet("/api/signals/41", () => Results.Text("""{"id":41,"kind":"start-requested","status":"pending","reason":null}""", "application/json"));
        });
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await (await ApplicationAsync(api, output, error, TimeSpan.FromMilliseconds(200))).RunAsync(["project", "start", "acme/shop"], Token);

        Assert.Equal(0, exit);
        Assert.Equal(Line("Signal 41 appended.") + Line("Signal 41 is still pending. Follow it with: signal show 41"), output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task A_Signal_the_loop_rejected_is_a_failure_that_says_why_on_the_error_stream()
    {
        await using var api = await StubApi.StartAsync(app =>
        {
            app.MapPost("/api/projects/stop", () => Results.Text("""{"id":9,"kind":"stop-requested","status":"pending","reason":null}""", "application/json", statusCode: 202));
            app.MapGet("/api/signals/9", () => Results.Text("""{"id":9,"kind":"stop-requested","status":"rejected","reason":"project-not-registered"}""", "application/json"));
        });
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await (await ApplicationAsync(api, output, error, TimeSpan.FromSeconds(5))).RunAsync(["project", "stop", "acme/shop"], Token);

        Assert.Equal(1, exit);
        Assert.Equal(Line("Signal 9 appended."), output.ToString());
        Assert.Equal(Line("Signal 9 was rejected: project-not-registered."), error.ToString());
    }

    [Fact]
    public async Task An_answer_that_is_not_a_Signal_is_reported_as_a_bad_answer_and_a_host_that_stops_answering_while_waiting_is_a_failure()
    {
        var polls = 0;
        await using var api = await StubApi.StartAsync(app =>
        {
            app.MapPost("/api/projects/start", () => Results.Text("""{"nothing":true}""", "application/json", statusCode: 202));
            app.MapPost("/api/projects/stop", () => Results.Text("""{"id":5,"kind":"stop-requested","status":"pending","reason":null}""", "application/json", statusCode: 202));
            app.MapGet("/api/signals/5", () => ++polls == 1 ? Results.Text("""{"id":5,"kind":"stop-requested","status":"pending","reason":null}""", "application/json") : Results.Problem(statusCode: 500));
        });
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = await ApplicationAsync(api, output, error, TimeSpan.FromSeconds(5));

        var bad = await application.RunAsync(["project", "start", "acme/shop"], Token);
        var failing = await application.RunAsync(["project", "stop", "acme/shop"], Token);

        Assert.Equal((1, 1), (bad, failing));
        Assert.Contains("Unexpected answer", error.ToString(), StringComparison.Ordinal);
    }
}
