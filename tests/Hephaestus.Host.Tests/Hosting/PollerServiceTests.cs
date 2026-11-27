using Hephaestus.GitHub;
using Hephaestus.Host.Hosting;
using Microsoft.Extensions.Logging;

namespace Hephaestus.Host.Tests.Hosting;

/// <summary>Story 2.1: the host runs the poller for its whole life and writes what a cycle found in the log, one line for each thing worth a line.</summary>
public class PollerServiceTests
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

    private static (PollerService Service, Capture Log, GitHubPoller Poller, PollerStores Stores) Build()
    {
        var capture = new Capture();
        var stores = new PollerStores();
        var poller = new GitHubPoller(stores, stores, stores, stores, TimeProvider.System, new GitHubRateLimit(TimeProvider.System), () => new HttpClientHandler(), (_, token) => Task.Delay(Timeout.Infinite, token));
        return (new PollerService(poller, capture), capture, poller, stores);
    }

    [Fact]
    public async Task The_service_runs_the_poller_until_the_host_stops_and_a_cycle_with_nothing_to_say_writes_no_line()
    {
        var (service, log, poller, stores) = Build();
        using var owner = poller;

        await service.StartAsync(TestContext.Current.CancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (stores.ProjectListings == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, stores.ProjectListings);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void A_problem_is_a_warning_and_new_Signals_are_an_information_line()
    {
        var (service, log, poller, stores) = Build();
        using var owner = poller;

        service.Report(new PollCycleReport(2, 3, ["acme/web: GitHub answered 404."], null));

        Assert.Contains(log.Lines, l => l.Level == LogLevel.Warning && l.Text == "Polling GitHub: acme/web: GitHub answered 404.");
        Assert.Contains(log.Lines, l => l.Level == LogLevel.Information && l.Text == "Polled 2 Project(s) and appended 3 Signal(s).");
    }

    [Fact]
    public void A_fault_that_ended_a_cycle_is_logged_whole_as_an_error_with_its_exception()
    {
        var (service, log, poller, _) = Build();
        using var owner = poller;
        var fault = new InvalidOperationException("the database is locked");

        service.Report(new PollCycleReport(0, 0, ["The cycle failed: InvalidOperationException: the database is locked"], null, fault));

        var line = Assert.Single(log.Lines);
        Assert.Equal((LogLevel.Error, fault), (line.Level, line.Exception));
    }

    [Fact]
    public void A_wait_GitHub_asked_for_is_said_once_with_the_time()
    {
        var (service, log, poller, stores) = Build();
        using var owner = poller;

        service.Report(new PollCycleReport(0, 0, [], new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero)));

        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains("2026-11-27T12:00:00.0000000+00:00", line.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_wait_that_comes_with_a_problem_is_only_the_problem()
    {
        var (service, log, poller, stores) = Build();
        using var owner = poller;

        service.Report(new PollCycleReport(0, 0, ["GitHub's rate limit is reached; waiting until 2026-11-27T12:00:00Z."], DateTimeOffset.UnixEpoch));

        Assert.Equal(LogLevel.Warning, Assert.Single(log.Lines).Level);
    }
}
