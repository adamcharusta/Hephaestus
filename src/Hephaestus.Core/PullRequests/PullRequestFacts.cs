using System.Text.Json;
using System.Text.Json.Serialization;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Results;
using Hephaestus.Core.Validation;

namespace Hephaestus.Core.PullRequests;

/// <summary>Who implemented a Task: the Agent, the Model, and the Effort a step run was given (FR-30). The Agent and the Effort are tags, which are never translated.</summary>
public sealed record PullRequestAgent(string Agent, string Model, string Effort)
{
    public static PullRequestAgent For(Agent agent, string model, Effort effort) => new(agent.Tag(), model, effort.Tag());
}

/// <summary>What became of one Validation command, as the pull request says it: the command, its status tag, and what the runtime saw of it, and nothing an Agent reported (FR-31).</summary>
/// <param name="Status">One of <see cref="ValidationCommandStatuses.Tag"/>; never translated.</param>
/// <param name="ExitCode">The exit status the runtime saw; null when none is known or the command was not run.</param>
/// <param name="DurationMilliseconds">How long the command ran; null when it was not run.</param>
public sealed record PullRequestCheck(string Command, string Status, int? ExitCode, long? DurationMilliseconds)
{
    public static PullRequestCheck From(ValidationCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new PullRequestCheck(result.Command, result.Status.Tag(), result.ExitCode, result.Duration is { } duration ? (long)duration.TotalMilliseconds : null);
    }
}

/// <summary>
/// What a pull request says, as the records hold it when the pull request is published (AD-19): the Task's title, the implementation summary, who implemented the Task, and the result of each Validation command. It is kept as
/// the payload of the publication's intent, so that a restart composes the same text. Everything an Agent wrote is neutralised when the text is composed, not here, so that the record stays what the Agent returned.
/// </summary>
/// <param name="Title">The issue's title, which is the pull request's.</param>
/// <param name="Summary">The summary the Agent returned with its result.</param>
/// <param name="Checks">The results of the Validation commands, in order, once Validation is decided.</param>
public sealed record PullRequestFacts(string Title, string Summary, PullRequestAgent Implementer, IReadOnlyList<PullRequestCheck> Checks)
{
    /// <summary>How long a summary the pull request carries; a longer one is cut where it ends, so that a description stays within what GitHub accepts.</summary>
    public const int MaximumSummaryLength = 10_000;

    /// <summary>The facts of a Task whose Validation is decided: the title, the result of the step that implemented it, who did, and the results of the Validation run.</summary>
    public static PullRequestFacts From(string title, ImplementationResult result, PullRequestAgent implementer, ValidationRun validation)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(implementer);
        ArgumentNullException.ThrowIfNull(validation);
        var summary = result.Summary.Length > MaximumSummaryLength ? result.Summary[..MaximumSummaryLength] : result.Summary;
        return new PullRequestFacts(title, summary, implementer, [.. validation.Results.Select(PullRequestCheck.From)]);
    }

    /// <summary>The payload of the intent that publishes these facts.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, PullRequestJson.Default.PullRequestFacts);

    /// <summary>Reads a payload; false when it is not one this version wrote.</summary>
    public static bool TryParse(string? payload, out PullRequestFacts facts)
    {
        facts = null!;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            facts = JsonSerializer.Deserialize(payload, PullRequestJson.Default.PullRequestFacts)!;
            return facts is { Title: not null, Summary: not null, Implementer: not null, Checks: not null };
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>Source-generated JSON of the facts of a pull request: camelCase keys, and a key the record does not have is an error, so that a payload that is not one of ours is never composed into text.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PullRequestFacts))]
internal sealed partial class PullRequestJson : JsonSerializerContext;
