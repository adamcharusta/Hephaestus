using System.Text.RegularExpressions;
using Hephaestus.Core.Effects;

namespace Hephaestus.Core.PullRequests;

/// <summary>
/// What the intent of a publication says (AD-5): the step run whose result is published and the tip of the Task branch it is published for. A Task that is published again after further work journals a new intent with its own step run, so the
/// same pull request is updated as an effect of its own. The facts the text is composed from are the payload of the intent, so the intent alone is what a restart resolves the publication from.
/// </summary>
/// <param name="StepRunId">The step run that ended the work being published.</param>
/// <param name="Tip">The commit the Task branch is at, which is the pull request's head.</param>
public sealed partial record PullRequestIntent(Guid StepRunId, string Tip)
{
    /// <summary>The detail that the journal records: at most 256 characters.</summary>
    public string Detail => string.Join('|', "pull-request", StepRunId.ToString("D"), Tip);

    /// <summary>The effect to journal for the Task <paramref name="taskId"/>, with the facts it is composed from.</summary>
    public NewEffect ToNewEffect(Guid taskId, PullRequestFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return new NewEffect(taskId, EffectKeys.StepCause(StepRunId), EffectKind.PullRequest, Detail, Payload: facts.ToJson());
    }

    /// <summary>Reads a detail; false when it is not one this version wrote.</summary>
    public static bool TryParse(string? detail, out PullRequestIntent intent)
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

        intent = new PullRequestIntent(stepRun, parts[2]);
        return true;
    }

    [GeneratedRegex(@"^pull-request\|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\|[0-9a-f]{40}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DetailPattern();
}
