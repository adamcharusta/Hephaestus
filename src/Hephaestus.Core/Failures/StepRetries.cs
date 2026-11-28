namespace Hephaestus.Core.Failures;

/// <summary>What becomes of a failed step: it is attempted again, or the Task ends, and why (AD-11, NFR-10, NFR-5).</summary>
public enum StepFailureEnd
{
    /// <summary>The step is attempted again as a new step run.</summary>
    Retry,

    /// <summary>The class does not spend the step retry bound: the Task ends.</summary>
    NotRetried,

    /// <summary>The step retry bound is spent: the Task ends.</summary>
    Exhausted,

    /// <summary>The Task's time in <c>Running</c> has reached its duration limit: no step is attempted again, and the Task ends.</summary>
    DurationLimit,

    /// <summary>The step ended well and left the tree as it found it, with nothing pushed before: there is nothing to publish, so the Task ends. The loop decides this itself; <see cref="StepRetries.Decide"/> never answers it.</summary>
    NoChange,
}

/// <summary>The step retry bound and the Task duration limit as they decide what follows a failed step run; pure, so that the decision is the same whenever it is made from the records.</summary>
public static class StepRetries
{
    /// <summary>
    /// How short of its end the Task's time may be and still be read as ended: a step's deadline is set a moment before the Task's clock is read, so the step that the runtime stopped at the Task's limit can find a few
    /// milliseconds left, and a retry with a second left could not do anything.
    /// </summary>
    public static readonly TimeSpan DurationSlack = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The end of a step run that failed with <paramref name="failure"/> as its <paramref name="attempt"/>th attempt (from 1), when the Task has <paramref name="remaining"/> of its duration limit left. A step is tried again
    /// up to <paramref name="bound"/> times, so its attempts are at most <paramref name="bound"/> + 1.
    /// </summary>
    public static StepFailureEnd Decide(FailureClass failure, int attempt, int bound, TimeSpan remaining)
    {
        if (!failure.SpendsStepRetry())
        {
            return StepFailureEnd.NotRetried;
        }

        if (remaining <= DurationSlack)
        {
            return StepFailureEnd.DurationLimit;
        }

        return attempt - 1 < bound ? StepFailureEnd.Retry : StepFailureEnd.Exhausted;
    }
}
