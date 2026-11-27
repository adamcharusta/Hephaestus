using Hephaestus.Core.Effects;
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
            _log.LogError(failure, "The orchestrator loop failed; the next tick tries again.");
        }

        foreach (var rejection in report.Rejections)
        {
            _log.LogWarning("{Rejection}", rejection);
        }

        foreach (var problem in report.Problems ?? [])
        {
            // In v0.1 this line is how the Owner learns of it: the issue can no longer be written to, so nothing can be said there.
            using var scope = _log.BeginScope(new Dictionary<string, object> { ["taskId"] = problem.TaskId });
            _log.LogError(
                "The effect {Key} of Task {TaskId} was not made ({Result}: {Reason}); {Outcome}.",
                problem.Key,
                problem.TaskId,
                problem.Result.Tag(),
                problem.Reason,
                problem.MovedToWaitingForOwner ? "the Task moved to waiting-for-owner" : "the Task keeps its state");
        }

        if (report.EffectsRetried > 0)
        {
            _log.LogWarning("{Retried} attempt(s) at an effect on GitHub did not succeed and will be tried again.", report.EffectsRetried);
        }

        if (report.Applied > 0)
        {
            _log.LogInformation("Applied {Applied} Signal(s) and created {Tasks} Task(s).", report.Applied, report.TasksCreated);
        }
    }
}
