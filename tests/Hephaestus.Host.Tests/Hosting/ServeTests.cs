using Hephaestus.Core.Localization;
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
        var environment = new Dictionary<string, string?>
        {
            [HostSettings.ControlSocketVariable] = socket.Path,
            [HostSettings.DataDirectoryVariable] = socket.DataDirectory,
            [HostSettings.SecretsDirectoryVariable] = socket.SecretsDirectory,
        };
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
        var environment = new Dictionary<string, string?>
        {
            [HostSettings.ControlSocketVariable] = socket.Path,
            [HostSettings.DataDirectoryVariable] = socket.DataDirectory,
            [HostSettings.SecretsDirectoryVariable] = socket.SecretsDirectory,
        };

        await Assert.ThrowsAnyAsync<Exception>(() => EntryPoint.RunAsync(["serve"], TextReader.Null, TextWriter.Null, TextWriter.Null, name => environment.GetValueOrDefault(name), TestContext.Current.CancellationToken));

        Assert.False(File.Exists(socket.Path));
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
