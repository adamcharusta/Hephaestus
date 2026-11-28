using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Validation;

/// <summary>What became of one Validation command (FR-31, FR-19, AD-18).</summary>
public enum ValidationCommandStatus
{
    /// <summary>The command exited with status 0.</summary>
    Passed,

    /// <summary>The command exited with a status other than 0.</summary>
    Failed,

    /// <summary>The runtime stopped the command at its deadline.</summary>
    TimedOut,

    /// <summary>The command's container was killed for using more memory than its limit, or was gone when the runtime looked, so no exit status of the command is known.</summary>
    Interrupted,

    /// <summary>An earlier command did not pass, so this one was never started.</summary>
    NotRun,
}

public static class ValidationCommandStatuses
{
    /// <summary>The name stored and shown for a status; never translated.</summary>
    public static string Tag(this ValidationCommandStatus status) => status switch
    {
        ValidationCommandStatus.Passed => "passed",
        ValidationCommandStatus.Failed => "failed",
        ValidationCommandStatus.TimedOut => "timeout",
        ValidationCommandStatus.Interrupted => "interrupted",
        ValidationCommandStatus.NotRun => "not-run",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}

/// <summary>
/// The recorded result of one Validation command: what the runtime saw of its container, and nothing an Agent reported (FR-31). Each command is its own container run, so it has its own step run, whose id is the
/// key of the one output file of the run (AD-18); a command that was not run has none.
/// </summary>
/// <param name="Index">The command's position in the Project's list, from 0.</param>
/// <param name="Command">The command as the Project wrote it.</param>
/// <param name="ExitCode">The exit status of the container's process as the runtime saw it; null when the command was not run or the container was gone, so that none is known.</param>
/// <param name="Duration">How long the container ran; null when the command was not run.</param>
/// <param name="StepRunId">The container run of the command, which keys its output file; null when it was not run.</param>
public sealed record ValidationCommandResult(int Index, string Command, ValidationCommandStatus Status, int? ExitCode, TimeSpan? Duration, Guid? StepRunId)
{
    /// <summary>
    /// The result of a command whose container ended, from what the step-exited Signal of its step run carries: the exit status and the runtime's cause. Only the command's own exit with status 0 passes. A command
    /// that ended with another status failed; one the runtime stopped at its deadline timed out, whatever status the stop gave it; one that ran out of memory or was lost is neither a pass nor a failure of the command.
    /// </summary>
    public static ValidationCommandResult Ended(int index, string command, Guid stepRunId, int? exitCode, StepExitCause cause, TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "A command cannot have run for less than no time.");
        }

        var status = cause switch
        {
            StepExitCause.Exited when exitCode is 0 => ValidationCommandStatus.Passed,
            StepExitCause.Exited when exitCode is not null => ValidationCommandStatus.Failed,
            StepExitCause.Timeout => ValidationCommandStatus.TimedOut,
            _ => ValidationCommandStatus.Interrupted,
        };
        return new ValidationCommandResult(index, command, status, exitCode, duration, stepRunId);
    }

    /// <summary>A command that was never started because an earlier one did not pass.</summary>
    public static ValidationCommandResult NotRun(int index, string command) => new(index, command, ValidationCommandStatus.NotRun, null, null, null);
}
