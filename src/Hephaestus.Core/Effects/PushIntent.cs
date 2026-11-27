using System.Text.RegularExpressions;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Effects;

/// <summary>
/// What the intent of a push says (AD-5, AD-9): the step run whose checkpoint it is, the checksum of the bundle, the tip it carries, the last pushed tip it must fast-forward, and the Task's start commit. The journal holds the intent
/// as its detail, so the intent is the record of the tip before the step run and the tip after it, and a push that was started and not recorded is resolved from it alone, after a restart. The file's path is the one the runtime chose
/// (<see cref="BundleReference.CheckpointPath"/>).
/// </summary>
/// <param name="StepRunId">The step run that was checkpointed.</param>
/// <param name="Sha256">The checksum of the bundle's file.</param>
/// <param name="Tip">The commit the bundle carries: the tip after the step run.</param>
/// <param name="LastPushedTip">The commit the bundle was made on: the tip before the step run.</param>
/// <param name="StartCommit">The commit on the Base Branch that the Task began from.</param>
public sealed partial record PushIntent(Guid StepRunId, string Sha256, string Tip, string LastPushedTip, string StartCommit)
{
    /// <summary>The intent of the push of the checkpoint that the runtime returned for a step run.</summary>
    public static PushIntent For(Guid stepRunId, BundleReference bundle, string startCommit)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return new PushIntent(
            stepRunId,
            bundle.Sha256,
            bundle.Tip,
            bundle.Prerequisite ?? throw new ArgumentException("The bundle of a checkpoint has the last pushed tip as its prerequisite.", nameof(bundle)),
            startCommit);
    }

    /// <summary>The detail that the journal records: at most 256 characters.</summary>
    public string Detail => string.Join('|', "push", StepRunId.ToString("D"), Sha256, Tip, LastPushedTip, StartCommit);

    /// <summary>The effect to journal, with the state change that causes it, for the Task <paramref name="taskId"/>.</summary>
    public NewEffect ToNewEffect(Guid taskId) => new(taskId, EffectKeys.StepCause(StepRunId), EffectKind.Push, Detail);

    /// <summary>The bundle the intent names, for the Task <paramref name="taskId"/>.</summary>
    public BundleReference Bundle(Guid taskId) => new(BundleReference.CheckpointPath(taskId, StepRunId), Sha256, Tip, LastPushedTip);

    /// <summary>Reads a detail; false when it is not one this version wrote.</summary>
    public static bool TryParse(string? detail, out PushIntent intent)
    {
        intent = null!;
        if (detail is null || !DetailPattern().IsMatch(detail))
        {
            return false;
        }

        var parts = detail.Split('|');
        if (!Guid.TryParseExact(parts[1], "D", out var stepRun) || stepRun == Guid.Empty)
        {
            return false;
        }

        intent = new PushIntent(stepRun, parts[2], parts[3], parts[4], parts[5]);
        return true;
    }

    [GeneratedRegex(@"^push\|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\|[0-9a-f]{64}\|[0-9a-f]{40}\|[0-9a-f]{40}\|[0-9a-f]{40}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DetailPattern();
}
