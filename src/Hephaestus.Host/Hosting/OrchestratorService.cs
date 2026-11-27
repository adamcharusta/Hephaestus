using Hephaestus.Core.Loop;

namespace Hephaestus.Host.Hosting;

/// <summary>
/// Runs the orchestrator loop for the life of the host (AD-1). It is transport only: the loop decides what each Signal does, and this
/// reports each tick in the log, where a tick that did nothing is not worth a line. The first tick is startup recovery: what was
/// appended and not applied before the host stopped is still pending.
/// </summary>
internal sealed class OrchestratorService(OrchestratorLoop loop, ILoggerFactory loggers) : BackgroundService
{
    private readonly ILogger _log = loggers.CreateLogger("Hephaestus.Loop");

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(() => loop.RunAsync(Report, stoppingToken), stoppingToken);

    internal void Report(TickReport report)
    {
        if (report.Failure is { } failure)
        {
            _log.LogError(failure, "The orchestrator loop failed to apply a Signal; the next tick tries again.");
        }

        foreach (var rejection in report.Rejections)
        {
            _log.LogWarning("{Rejection}", rejection);
        }

        if (report.Applied > 0)
        {
            _log.LogInformation("Applied {Applied} Signal(s) and created {Tasks} Task(s).", report.Applied, report.TasksCreated);
        }
    }
}
