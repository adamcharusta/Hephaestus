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
            // An effect on an issue that is no Task has no Task's id; the log names the repository and the issue instead.
            using var scope = problem.TaskId is { } taskId ? _log.BeginScope(new Dictionary<string, object> { ["taskId"] = taskId }) : null;
            _log.LogError(
                "The effect {Key} of {Subject} was not made ({Result}: {Reason}); {Outcome}.",
                problem.Key,
                problem.TaskId is { } id ? $"Task {id}" : $"an issue that is no Task (repository {problem.RepositoryId}, issue #{problem.IssueNumber})",
                problem.Result.Tag(),
                problem.Reason,
                problem.MovedToWaitingForOwner ? "the Task moved to waiting-for-owner" : problem.TaskId is null ? "no Task is affected" : "the Task keeps its state");
        }

        foreach (var problem in report.TaskProblems ?? [])
        {
            // Until a Task's issue says so (Story 3.15), the log is how the Owner learns that a Task could not be started, or that one ended because a step could not be carried out.
            _log.LogWarning("{Problem}", problem);
        }

        if (report.TasksStarted > 0)
        {
            _log.LogInformation("Started {Started} Task(s).", report.TasksStarted);
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
