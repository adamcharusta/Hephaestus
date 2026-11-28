using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hephaestus.Core.Effects;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Localization;

namespace Hephaestus.Core.Failures;

/// <summary>How a Task ended as <c>Failed</c>, in the words the comment tells it by; the tags are stored and never translated.</summary>
public static class TaskFailureEnds
{
    public const string NotRetried = "not-retried";
    public const string Exhausted = "exhausted";
    public const string DurationLimit = "duration-limit";
    public const string NoChange = "no-change";

    /// <summary>The tag of an end; a retry is no end of a Task.</summary>
    public static string Tag(this StepFailureEnd end) => end switch
    {
        StepFailureEnd.NotRetried => NotRetried,
        StepFailureEnd.Exhausted => Exhausted,
        StepFailureEnd.DurationLimit => DurationLimit,
        StepFailureEnd.NoChange => NoChange,
        _ => throw new ArgumentOutOfRangeException(nameof(end), end, "A step that is attempted again does not end the Task."),
    };
}

/// <summary>
/// What the comment on the issue of a failed Task says, as the records hold it when the Task ends (AD-19, UX-DR-19): the class that ended it, the step and the command that failed, how many attempts were made, and the Task branch that holds the work.
/// It is the payload of the intent of the comment, so that a restart composes the same text.
/// </summary>
/// <param name="Failure">The class of the failure that ended the Task, as <see cref="FailureClasses.Tag"/> names it.</param>
/// <param name="Step">The kind of step that failed, as a tag.</param>
/// <param name="Command">The Validation command that failed; null for a step of an Agent. It is the Project's own text, neutralised when the comment is composed.</param>
/// <param name="ExitCode">The exit status of the command, when the runtime saw one.</param>
/// <param name="Attempts">How many step runs of this step were made, from 1.</param>
/// <param name="Retries">How many times the step could have been tried again.</param>
/// <param name="End">One of <see cref="TaskFailureEnds"/>.</param>
/// <param name="DurationLimit">The Task duration limit that was reached, in its written form, for the end <see cref="TaskFailureEnds.DurationLimit"/>.</param>
/// <param name="Branch">The Task branch, which holds all work done.</param>
public sealed record TaskFailureFacts(string Failure, string Step, string? Command, int? ExitCode, int Attempts, int Retries, string End, string? DurationLimit, string Branch)
{
    /// <summary>The payload of the intent that posts these facts.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, TaskFailureJson.Default.TaskFailureFacts);

    /// <summary>Reads a payload; false when it is not one this version wrote.</summary>
    public static bool TryParse(string? payload, out TaskFailureFacts facts)
    {
        facts = null!;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            facts = JsonSerializer.Deserialize(payload, TaskFailureJson.Default.TaskFailureFacts)!;
            return facts is { Failure: not null, Step: not null, End: not null, Branch: not null } && FailureClasses.TryParse(facts.Failure, out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>The comment that says a Task failed (Story 3.16): the fixed shape of <c>EXPERIENCE.md</c>, a headline, the facts, and what happens next, with the fixed words from the resources in the language of the text (AD-21).</summary>
public static class TaskFailureNotice
{
    /// <summary>The detail of the intent of the comment.</summary>
    public const string Detail = "task-failed";

    /// <summary>The comment to journal for the Task <paramref name="taskId"/>, whose issue it is posted on.</summary>
    public static NewEffect ToNewEffect(Guid taskId, string cause, long repositoryId, int issueNumber, TaskFailureFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return new NewEffect(taskId, cause, EffectKind.Comment, Detail, repositoryId, issueNumber, Payload: facts.ToJson());
    }

    /// <summary>The text of the comment whose intent is <paramref name="effect"/>; false when the intent is not this kind of comment or its facts are not ones this version wrote.</summary>
    public static bool TryWrite(Effect effect, Language language, out string text)
    {
        ArgumentNullException.ThrowIfNull(effect);
        text = string.Empty;
        if (effect.Detail != Detail || !TaskFailureFacts.TryParse(effect.Payload, out var facts))
        {
            return false;
        }

        text = Compose(facts, language);
        return true;
    }

    /// <summary>The comment, without its hidden marker, which the gateway adds last. What the Project's file wrote is neutralised, like anything a repository can name (AD-19).</summary>
    public static string Compose(TaskFailureFacts facts, Language language)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var catalog = MessageCatalog.Instance;
        var text = new StringBuilder();
        text.Append(catalog.Resolve(new Message(MessageIds.CommentFailedHeadline, facts.Failure, facts.Step), language)).Append("\n\n");
        if (facts.Command is { } command)
        {
            var shown = AgentText.Neutralise(command).ReplaceLineEndings(" ").Replace('`', '\'');
            text.Append(facts.ExitCode is { } exit
                ? catalog.Resolve(new Message(MessageIds.CommentFailedCommandExit, shown, exit), language)
                : catalog.Resolve(new Message(MessageIds.CommentFailedCommand, shown), language)).Append('\n');
        }

        text.Append(facts.End switch
        {
            TaskFailureEnds.Exhausted => catalog.Resolve(new Message(MessageIds.CommentFailedExhausted, facts.Attempts, facts.Retries), language),
            TaskFailureEnds.DurationLimit => catalog.Resolve(new Message(MessageIds.CommentFailedDurationLimit, facts.Attempts, facts.DurationLimit ?? string.Empty), language),
            TaskFailureEnds.NoChange => catalog.Resolve(new Message(MessageIds.CommentFailedNoChange, facts.Attempts), language),
            _ => catalog.Resolve(new Message(MessageIds.CommentFailedNotRetried, facts.Attempts), language),
        });
        return text.Append("\n\n").Append(catalog.Resolve(new Message(MessageIds.CommentFailedNext, facts.Branch, Labels.Marker), language)).ToString();
    }
}

/// <summary>Source-generated JSON of the facts of a failure: camelCase keys, and a key the record does not have is an error, so that a payload that is not one of ours is never composed into text.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TaskFailureFacts))]
internal sealed partial class TaskFailureJson : JsonSerializerContext;
