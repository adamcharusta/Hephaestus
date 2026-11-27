using Hephaestus.Core.Loop;
using Hephaestus.Core.Ports;
using Hephaestus.Host.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hephaestus.Host.Tests.Hosting;

/// <summary>Story 2.2: the host runs the orchestrator loop for its whole life and writes what a tick did in the log.</summary>
public class OrchestratorServiceTests
{
    private sealed record Line(LogLevel Level, string Text, Exception? Exception);

    private sealed class Capture : ILoggerFactory, ILogger
    {
        public List<Line> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(new Line(logLevel, formatter(state, exception), exception));

        public void Dispose()
        {
        }
    }

    private static (OrchestratorService Service, Capture Log, LoopHeartbeat Heartbeat) Build()
    {
        var capture = new Capture();
        var stores = new PollerStores();
        var heartbeat = new LoopHeartbeat(TimeProvider.System);
        var loop = new OrchestratorLoop(stores, stores, new NoWrites(), TimeProvider.System, heartbeat, (_, token) => Task.Delay(Timeout.Infinite, token));
        return (new OrchestratorService(loop, capture), capture, heartbeat);
    }

    private sealed class NoWrites : IUnitOfWork
    {
        public Task<bool> ApplyAsync(SignalApplication application, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    [Fact]
    public async Task The_service_runs_the_loop_until_the_host_stops_and_a_tick_with_nothing_to_say_writes_no_line()
    {
        var (service, log, heartbeat) = Build();

        await service.StartAsync(TestContext.Current.CancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (heartbeat.Check().LastTickAt is null && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(heartbeat.Check().LastTickAt);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void Applied_Signals_and_the_Tasks_they_made_are_one_information_line()
    {
        var (service, log, _) = Build();

        service.Report(new TickReport(3, 0, 2, null, []));

        var line = Assert.Single(log.Lines);
        Assert.Equal((LogLevel.Information, "Applied 3 Signal(s) and created 2 Task(s)."), (line.Level, line.Text));
    }

    [Fact]
    public void Each_rejected_Signal_is_a_warning_that_names_the_actor_and_the_reason()
    {
        var (service, log, _) = Build();

        service.Report(new TickReport(0, 1, 0, null, ["Signal 4 of actor 500 rejected: actor-not-trusted"]));

        var line = Assert.Single(log.Lines);
        Assert.Equal((LogLevel.Warning, "Signal 4 of actor 500 rejected: actor-not-trusted"), (line.Level, line.Text));
    }

    [Fact]
    public void A_failure_is_logged_whole_as_an_error_with_its_exception()
    {
        var (service, log, _) = Build();
        var fault = new InvalidOperationException("the database is locked");

        service.Report(new TickReport(0, 0, 0, fault, []));

        var line = Assert.Single(log.Lines);
        Assert.Equal((LogLevel.Error, fault), (line.Level, line.Exception));
    }

    [Fact]
    public async Task The_running_host_has_the_loop_service_over_one_heartbeat_and_the_unit_of_work_of_Persistence()
    {
        await using var host = await RunningHost.StartAsync();

        Assert.Single(host.App.Services.GetServices<IHostedService>().OfType<OrchestratorService>());
        Assert.Same(host.App.Services.GetRequiredService<LoopHeartbeat>(), host.App.Services.GetRequiredService<LoopHeartbeat>());
        Assert.NotNull(host.App.Services.GetRequiredService<OrchestratorLoop>());
        Assert.NotNull(host.App.Services.GetRequiredService<IUnitOfWork>());
    }
}
