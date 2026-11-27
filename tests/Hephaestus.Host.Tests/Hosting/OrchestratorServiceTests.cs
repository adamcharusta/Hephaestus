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
        var loop = new OrchestratorLoop(stores, stores, new NoWrites(), new FakeCodeHost(), new NoOpNotifier(), TimeProvider.System, heartbeat, (_, token) => Task.Delay(Timeout.Infinite, token));
        return (new OrchestratorService(loop, capture), capture, heartbeat);
    }

    private sealed class NoWrites : IUnitOfWork
    {
        public Task<bool> ApplyAsync(SignalApplication application, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> RecordEffectAsync(Hephaestus.Core.Effects.EffectRecording recording, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> EscalateAsync(Hephaestus.Core.Escalations.EscalationPlan plan, CancellationToken cancellationToken) => Task.FromResult(false);
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
    public void An_effect_that_could_not_be_made_is_an_error_that_names_the_Task_the_effect_and_what_became_of_the_Task()
    {
        var (service, log, _) = Build();
        var task = Guid.Parse("0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b");

        service.Report(new TickReport(0, 0, 0, null, [], 1, 0, [new EffectProblem(task, $"{task}:g1:label:0", Hephaestus.Core.Effects.EffectResult.Refused, "issue-not-found", MovedToWaitingForOwner: true)]));

        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Equal($"The effect {task}:g1:label:0 of Task {task} was not made (refused: issue-not-found); the Task moved to waiting-for-owner.", line.Text);
    }

    [Fact]
    public void An_effect_that_could_not_be_made_for_a_Task_that_stays_as_it_is_says_so()
    {
        var (service, log, _) = Build();

        service.Report(new TickReport(0, 0, 0, null, [], 1, 0, [new EffectProblem(Guid.Empty, "k", Hephaestus.Core.Effects.EffectResult.GaveUp, "unreachable", MovedToWaitingForOwner: false)]));

        Assert.EndsWith("the Task keeps its state.", Assert.Single(log.Lines).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_effect_that_could_not_be_made_for_an_issue_that_is_no_Task_is_an_error_that_names_the_effect_the_repository_and_the_issue()
    {
        var (service, log, _) = Build();

        service.Report(new TickReport(0, 0, 0, null, [], 1, 0, [new EffectProblem(null, "project:101:g1:comment:0", Hephaestus.Core.Effects.EffectResult.GaveUp, "unreachable", MovedToWaitingForOwner: false, RepositoryId: 101, IssueNumber: 7)]));

        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Equal("The effect project:101:g1:comment:0 of an issue that is no Task (repository 101, issue #7) was not made (gave-up: unreachable); no Task is affected.", line.Text);
    }

    [Fact]
    public void Attempts_that_will_be_tried_again_are_one_warning_and_a_tick_with_nothing_to_say_still_writes_no_line()
    {
        var (service, log, _) = Build();

        service.Report(new TickReport(0, 0, 0, null, [], 0, 2));
        service.Report(new TickReport(0, 0, 0, null, [], 1, 0));

        var line = Assert.Single(log.Lines);
        Assert.Equal((LogLevel.Warning, "2 attempt(s) at an effect on GitHub did not succeed and will be tried again."), (line.Level, line.Text));
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
