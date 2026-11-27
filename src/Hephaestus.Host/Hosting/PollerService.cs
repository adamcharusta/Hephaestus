using Hephaestus.GitHub;

namespace Hephaestus.Host.Hosting;

/// <summary>
/// Runs the gateway's poller for the life of the host (AD-4). It is transport only: the poller decides what to read and what to append,
/// and this reports each cycle in the log, where a cycle that appended nothing and met no trouble is not worth a line.
/// </summary>
internal sealed class PollerService(GitHubPoller poller, ILoggerFactory loggers) : BackgroundService
{
    private readonly ILogger _log = loggers.CreateLogger("Hephaestus.Poller");

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(() => poller.RunAsync(Report, stoppingToken), stoppingToken);

    internal void Report(PollCycleReport report)
    {
        if (report.Failure is { } failure)
        {
            _log.LogError(failure, "Polling GitHub failed; the next cycle tries again.");
            return;
        }

        foreach (var problem in report.Problems)
        {
            _log.LogWarning("Polling GitHub: {Problem}", problem);
        }

        if (report.SignalsAppended > 0)
        {
            _log.LogInformation("Polled {Projects} Project(s) and appended {Signals} Signal(s).", report.ProjectsRead, report.SignalsAppended);
        }
        else if (report.WaitUntil is { } until && report.Problems.Count == 0)
        {
            _log.LogInformation("Polling waits until {Until:O}, as GitHub asked.", until);
        }
    }
}
