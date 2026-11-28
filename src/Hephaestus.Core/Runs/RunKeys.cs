using Hephaestus.Core.Effects;

namespace Hephaestus.Core.Runs;

/// <summary>
/// The idempotency keys of what the runtime creates for a step run (AD-5, Consistency Conventions): the Task, the step run's cause, what is made, and its ordinal. The runtime records the key on the object it
/// creates and finds the object again by its labels, so a step run asked for twice after a crash makes one.
/// </summary>
public static class RunKeys
{
    public static string Volume(Guid taskId, Guid stepRunId) => Key(taskId, stepRunId, "volume");

    public static string Checkout(Guid taskId, Guid stepRunId) => Key(taskId, stepRunId, "checkout");

    public static string Container(Guid taskId, Guid stepRunId) => Key(taskId, stepRunId, "container");

    public static string Checkpoint(Guid taskId, Guid stepRunId) => Key(taskId, stepRunId, "checkpoint");

    private static string Key(Guid taskId, Guid stepRunId, string what) => $"{taskId:D}:{EffectKeys.StepCause(stepRunId)}:{what}:0";
}
