using Hephaestus.Core.Aliases;
using Hephaestus.Core.Effects;
using Hephaestus.Core.Failures;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Runs;

/// <summary>
/// Where a step run is. The loop never awaits a step (AD-18): it records that the run is planned, then that its container started, then, when the runtime's step-exited
/// Signal is applied, that it ended; what follows is decided from these records, so a tick can stop between any two of them and the next one carries on.
/// </summary>
public enum StepRunStatus
{
    /// <summary>Recorded, with its deadline, and its container not yet asked for (AD-18: the deadline is recorded before the container starts).</summary>
    Planned,

    /// <summary>The runtime has the container started.</summary>
    Started,

    /// <summary>The container ended and the Signal that says so was applied; the run records how.</summary>
    Ended,

    /// <summary>An Implementation run whose work was kept as a commit; the push of it is journaled in the same transaction (AD-9).</summary>
    Checkpointed,

    /// <summary>The loop has acted on the end of the run for good, and its consequence (the publication, or the end of the Task) is journaled with it: the last Validation command, or an Implementation that left the tree as it found it.</summary>
    Concluded,
}

public static class StepRunStatuses
{
    /// <summary>The name stored and shown; never translated.</summary>
    public static string Tag(this StepRunStatus status) => status switch
    {
        StepRunStatus.Planned => "planned",
        StepRunStatus.Started => "started",
        StepRunStatus.Ended => "ended",
        StepRunStatus.Checkpointed => "checkpointed",
        StepRunStatus.Concluded => "concluded",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static bool TryParse(string? tag, out StepRunStatus status)
    {
        foreach (var candidate in Enum.GetValues<StepRunStatus>())
        {
            if (candidate.Tag() == tag)
            {
                status = candidate;
                return true;
            }
        }

        status = default;
        return false;
    }

    /// <summary>The statuses a run may be in when it moves to <paramref name="to"/>: a run only goes forward, and a second record of the same change finds the run already moved and changes nothing.</summary>
    public static IReadOnlyList<StepRunStatus> MayMoveToFrom(this StepRunStatus to) => to switch
    {
        StepRunStatus.Started => [StepRunStatus.Planned],
        StepRunStatus.Ended => [StepRunStatus.Planned, StepRunStatus.Started],
        StepRunStatus.Checkpointed => [StepRunStatus.Ended],
        StepRunStatus.Concluded => [StepRunStatus.Ended, StepRunStatus.Checkpointed],
        _ => [],
    };
}

/// <summary>
/// One step of a Task, as the loop recorded it (AD-1). It names the Agent, Model, and Effort that performed it, the Methodology and template an Agent step was given, and the tips
/// before and after (AD-9), and how it ended. Written only by the loop, through <c>IUnitOfWork</c>.
/// </summary>
/// <param name="Sequence">The step's place in the Task's history, from 1.</param>
/// <param name="Kind">What the step is for; <see cref="StepKind.Validation"/> for one command of Validation.</param>
/// <param name="Round">Which round of Correction the step belongs to, from 0 (AD-11).</param>
/// <param name="Deadline">When the runtime stops the container; recorded before it starts.</param>
/// <param name="TipBefore">The last pushed tip the step's checkout was made from.</param>
/// <param name="CommandIndex">For a Validation step, which of the snapshot's commands it runs.</param>
/// <param name="Failure">How the step failed, as one of the <see cref="FailureClasses"/>; null when it did not.</param>
/// <param name="Outcome">What the step's result said: <c>completed</c> or <c>needs-decision</c> for an Agent step, and the command's status for a Validation step.</param>
/// <param name="Result">The result document of an Agent step as it was validated, which later decisions read.</param>
/// <param name="TipAfter">The tip of the checkpoint, when the step's work was kept.</param>
/// <param name="Attempt">Which attempt at the step this run is, from 1: a step that failed in a class that spends the step retry bound is attempted again as a new step run with the next number (AD-11).</param>
public sealed record StepRun(
    Guid Id,
    Guid TaskId,
    int Sequence,
    StepKind Kind,
    int Round,
    StepRunStatus Status,
    DateTimeOffset Deadline,
    DateTimeOffset PlannedAt,
    string TipBefore,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? EndedAt = null,
    Agent? Agent = null,
    string? Model = null,
    Effort? Effort = null,
    string? Methodology = null,
    string? Template = null,
    int? CommandIndex = null,
    int? ExitCode = null,
    StepExitCause? Cause = null,
    string? Failure = null,
    string? Outcome = null,
    string? Result = null,
    string? TipAfter = null,
    int Attempt = 1)
{
    public bool IsValidation => Kind == StepKind.Validation;

    /// <summary>How long the container ran, when it ended: from the moment the runtime started it, or, when that was not recorded, from the plan.</summary>
    public TimeSpan? Duration => EndedAt is { } ended ? ended - (StartedAt ?? PlannedAt) : null;
}

/// <summary>A step run the loop plans: the record that is written before its container is asked for.</summary>
public sealed record NewStepRun(
    Guid Id,
    Guid TaskId,
    StepKind Kind,
    int Round,
    DateTimeOffset Deadline,
    DateTimeOffset PlannedAt,
    string TipBefore,
    Agent? Agent = null,
    string? Model = null,
    Effort? Effort = null,
    string? Methodology = null,
    string? Template = null,
    int? CommandIndex = null,
    int Attempt = 1);

/// <summary>How a step run ended, as the runtime's cause, the Agent's result, and Core's reading of them say.</summary>
/// <param name="Failure">The class of the failure, or null when the step did what it was asked.</param>
public sealed record StepRunEnd(DateTimeOffset At, int? ExitCode, StepExitCause Cause, FailureClass? Failure, string? Outcome, string? Result);

/// <summary>
/// One change of a step run, committed in one transaction with what it causes (AD-1): the intents of effects, and the move of the Task that goes with it. The change is made only while the run is in a status
/// <see cref="StepRunStatuses.MayMoveToFrom"/> allows, so recording it twice changes nothing the second time, and the move is made only while the Task is in the state it leaves.
/// </summary>
/// <param name="To">The status the run moves to.</param>
/// <param name="End">How the run ended, for a change to <see cref="StepRunStatus.Ended"/>.</param>
/// <param name="TipAfter">The tip of the checkpoint, for a change to <see cref="StepRunStatus.Checkpointed"/>.</param>
/// <param name="Effects">The intents the change causes, for example the push of a checkpoint.</param>
/// <param name="Move">The move of the Task the change causes, for example to <c>Failed</c>, with the intents of its label.</param>
/// <param name="Failure">The class of a failure the loop found after the step ended, for example that it left no change; recorded on the run when set.</param>
/// <param name="Retry">The step run that attempts the failed step again, planned in the same transaction, only while the Task is <c>Running</c>.</param>
public sealed record StepRunChange(Guid StepRunId, StepRunStatus To, StepRunEnd? End = null, string? TipAfter = null, IReadOnlyList<NewEffect>? Effects = null, TaskMove? Move = null, FailureClass? Failure = null, NewStepRun? Retry = null);
