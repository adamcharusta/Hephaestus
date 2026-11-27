using Hephaestus.Core.Localization;
using Hephaestus.Workspaces.Tests;
using Hephaestus.Host.Cli;
using Hephaestus.Host.Hosting;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Hephaestus.Host.Tests.Hosting;

public class ServeTests
{
    [Fact]
    public async Task Serve_runs_the_host_on_the_environments_socket_until_it_is_cancelled_and_then_returns_0()
    {
        using var socket = new TempSocket();
        socket.PrepareAgents();
        var environment = socket.Environment();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var serving = EntryPoint.RunAsync(["serve"], TextReader.Null, TextWriter.Null, TextWriter.Null, name => environment.GetValueOrDefault(name), cancellation.Token);
        using var client = new ControlApiClient(socket.Path, Language.English);
        var answer = await AnswerWhenListeningAsync(client);
        await cancellation.CancelAsync();

        Assert.True(answer.IsSuccess);
        Assert.True(File.Exists(Path.Combine(socket.DataDirectory, "hephaestus.db")));
        Assert.Equal(0, await serving);
    }

    [Fact]
    public async Task Logs_are_json_objects_written_through_the_console_logger_to_standard_output()
    {
        await using var host = await RunningHost.StartAsync();

        var console = host.App.Services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue;
        var json = host.App.Services.GetRequiredService<IOptionsMonitor<MaskedJsonFormatterOptions>>().CurrentValue;

        Assert.Equal(MaskedJsonFormatter.FormatterName, console.FormatterName);
        Assert.True(json.UseUtcTimestamp);
        Assert.True(json.IncludeScopes);
        Assert.Contains(host.App.Services.GetServices<ILoggerProvider>(), p => p is ConsoleLoggerProvider);
        Assert.Single(host.App.Services.GetServices<ILoggerProvider>());
    }

    [Fact]
    public async Task A_migration_that_cannot_run_stops_the_host_before_it_listens()
    {
        using var socket = new TempSocket();
        // A file where the data directory should be: the database cannot be created, so the host must not serve.
        await File.WriteAllTextAsync(socket.DataDirectory, "not a directory", TestContext.Current.CancellationToken);
        socket.PrepareAgents();
        var environment = socket.Environment();

        await Assert.ThrowsAnyAsync<Exception>(() => EntryPoint.RunAsync(["serve"], TextReader.Null, TextWriter.Null, TextWriter.Null, name => environment.GetValueOrDefault(name), TestContext.Current.CancellationToken));

        Assert.False(File.Exists(socket.Path));
    }

    [Fact]
    public async Task Serve_places_the_certificate_bundle_of_the_image_in_the_agents_volume_before_it_listens()
    {
        using var socket = new TempSocket();
        socket.PrepareAgents();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var serving = EntryPoint.RunAsync(["serve"], TextReader.Null, TextWriter.Null, TextWriter.Null, name => socket.Environment().GetValueOrDefault(name), cancellation.Token);
        using var client = new ControlApiClient(socket.Path, Language.English);
        await AnswerWhenListeningAsync(client);
        await cancellation.CancelAsync();
        await serving;

        Assert.Equal("certificates of the image", await File.ReadAllTextAsync(Path.Combine(socket.AgentsDirectory, "certs", "ca-certificates.crt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_download_that_cannot_be_made_stops_startup_before_the_host_listens_and_names_the_executable()
    {
        using var socket = new TempSocket();
        var vendor = socket.PrepareAgents();
        Directory.Delete(socket.AgentsDirectory, recursive: true);
        // Nothing listens on the discard port, so a download from here is refused at once.
        await File.WriteAllTextAsync(socket.AgentsLockFile, vendor.LockText.Replace("https://vendor.test", "https://127.0.0.1:9", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        var error = new StringWriter();

        var exitCode = await EntryPoint.RunAsync(["serve"], TextReader.Null, TextWriter.Null, error, name => socket.Environment().GetValueOrDefault(name), TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Contains("Startup stopped", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("claude-code/", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(socket.Path));
        Assert.False(Directory.Exists(Path.Combine(socket.AgentsDirectory, ".staging")));
        // The volume is prepared after the database is migrated (AR-40): the migration had already run when the start stopped.
        Assert.True(File.Exists(Path.Combine(socket.DataDirectory, "hephaestus.db")));
    }

    [Fact]
    public async Task Startup_says_why_in_the_installations_language()
    {
        using var socket = new TempSocket();
        var vendor = socket.PrepareAgents();
        await File.WriteAllTextAsync(socket.AgentsLockFile, vendor.LockText.Replace("claude-code", "claude-coda", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        var environment = socket.Environment();
        environment[HostSettings.LanguageVariable] = "pl";
        var error = new StringWriter();

        var exitCode = await EntryPoint.RunAsync(["serve"], TextReader.Null, TextWriter.Null, error, name => environment.GetValueOrDefault(name), TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.StartsWith("Uruchamianie wstrzymane", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("agents.lock", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("claude-coda", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lock_that_cannot_be_read_stops_startup()
    {
        using var socket = new TempSocket();
        socket.PrepareAgents();
        File.Delete(socket.AgentsLockFile);
        var error = new StringWriter();

        var exitCode = await EntryPoint.RunAsync(["serve"], TextReader.Null, TextWriter.Null, error, name => socket.Environment().GetValueOrDefault(name), TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Contains(socket.AgentsLockFile, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_executable_that_does_not_match_the_lock_is_not_left_in_the_volume_when_it_cannot_be_replaced()
    {
        using var socket = new TempSocket();
        var vendor = socket.PrepareAgents();
        var tampered = Path.Combine(socket.AgentsDirectory, "claude-code", "x64-glibc", "claude");
        var tamperedArm = Path.Combine(socket.AgentsDirectory, "claude-code", "arm64-glibc", "claude");
        var path = File.Exists(tampered) ? tampered : tamperedArm;
        await File.WriteAllTextAsync(path, "tampered", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(socket.AgentsLockFile, vendor.LockText.Replace("https://vendor.test", "https://127.0.0.1:9", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        var exitCode = await EntryPoint.RunAsync(["serve"], TextReader.Null, TextWriter.Null, TextWriter.Null, name => socket.Environment().GetValueOrDefault(name), TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(path));
    }

    private static async Task<ApiAnswer> AnswerWhenListeningAsync(ControlApiClient client)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await client.GetAsync("/api/version", TestContext.Current.CancellationToken);
            }
            catch (HttpRequestException) when (attempt < 100)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }
    }
}
