using Hephaestus.Core.Effects;
using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Escalations;

/// <summary>What an Escalation asks (FR-48).</summary>
public enum EscalationKind
{
    /// <summary>A decision Hephaestus cannot make on its own; the Agent's question and options are the text.</summary>
    Decision,

    /// <summary>An effect on GitHub reached its retry bound or was refused for good (AD-5); the effect and why are the facts, and the options are fixed.</summary>
    EffectFailed,
}

public static class EscalationKinds
{
    /// <summary>The name stored; never translated.</summary>
    public static string Tag(this EscalationKind kind) => kind switch
    {
        EscalationKind.Decision => "decision",
        EscalationKind.EffectFailed => "effect-failed",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static bool TryParse(string? tag, out EscalationKind kind)
    {
        foreach (var candidate in Enum.GetValues<EscalationKind>())
        {
            if (candidate.Tag() == tag)
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }
}

/// <summary>How an Escalation ended without an answer: the Task left <c>WaitingForOwner</c> because a Trusted User cancelled or closed it.</summary>
public enum EscalationOutcome
{
    /// <summary>A Trusted User removed the Marker Label, and the Task became <c>Cancelled</c>.</summary>
    Cancelled,

    /// <summary>A Trusted User closed the issue, and the Task became <c>Closed</c>.</summary>
    Closed,
}

public static class EscalationOutcomes
{
    public static string Tag(this EscalationOutcome outcome) => outcome switch
    {
        EscalationOutcome.Cancelled => "cancelled",
        EscalationOutcome.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    public static bool TryParse(string? tag, out EscalationOutcome outcome)
    {
        foreach (var candidate in Enum.GetValues<EscalationOutcome>())
        {
            if (candidate.Tag() == tag)
            {
                outcome = candidate;
                return true;
            }
        }

        outcome = default;
        return false;
    }
}

/// <summary>The effect an <see cref="EscalationKind.EffectFailed"/> Escalation is about, as the journal holds it.</summary>
/// <param name="Reason">One of <see cref="EffectReasons"/>.</param>
public sealed record FailedEffect(string Key, EffectKind Kind, string Detail, string Reason);

/// <summary>
/// A question Hephaestus asked the Owner about a Task, recorded on the Task (FR-48, AD-1). Its text is kept as it came, and is neutralised
/// when the comment is made (AD-19).
/// </summary>
/// <param name="Decision">The decision needed, in one sentence; empty for an effect that could not be completed, whose sentence is fixed.</param>
/// <param name="Options">The options Hephaestus sees, in order; empty for an effect that could not be completed, whose options are fixed.</param>
/// <param name="ResumeCondition">What ends the wait, one of <see cref="ResumeConditions"/> (AD-1: every wait has one).</param>
/// <param name="Cause">What caused it, as the journal names causes.</param>
/// <param name="Effect">The effect that could not be completed, when that is the reason.</param>
/// <param name="ClosedAt">When the Task left <c>WaitingForOwner</c> without an answer; null while the Escalation is open.</param>
public sealed record Escalation(
    Guid Id,
    Guid TaskId,
    EscalationKind Kind,
    string Decision,
    IReadOnlyList<string> Options,
    string ResumeCondition,
    string Cause,
    DateTimeOffset RaisedAt,
    FailedEffect? Effect = null,
    DateTimeOffset? ClosedAt = null,
    EscalationOutcome? Outcome = null);

/// <summary>What ends a wait; machine values, never translated.</summary>
public static class ResumeConditions
{
    /// <summary>A reply on the issue from a Trusted User, which returns the Task to the Queue with the answer (Story 2.10).</summary>
    public const string TrustedReply = "trusted-reply";
}

/// <summary>
/// What a caller that cannot go on asks: one decision, and the options it sees. Both are one line each, bounded; a caller that has more
/// to say splits it into a decision that can be answered with a letter.
/// </summary>
public sealed record EscalationRequest
{
    /// <summary>The longest decision, in characters.</summary>
    public const int MaximumDecisionLength = 500;

    /// <summary>The longest option, in characters.</summary>
    public const int MaximumOptionLength = 300;

    /// <summary>Options are lettered A to Z.</summary>
    public const int MaximumOptions = 26;

    private EscalationRequest(string decision, IReadOnlyList<string> options)
    {
        Decision = decision;
        Options = options;
    }

    public string Decision { get; }

    public IReadOnlyList<string> Options { get; }

    /// <summary>
    /// The request for one decision. Line breaks and runs of spaces become one space, so that the decision is one line and each option is
    /// one line; text over the bound is cut with an ellipsis, not refused, because an Agent's question must reach the Owner.
    /// </summary>
    /// <exception cref="ArgumentException">The decision is empty, an option is empty, or there are more than <see cref="MaximumOptions"/> options.</exception>
    public static EscalationRequest For(string decision, IEnumerable<string> options)
    {
        var line = OneLine(decision, MaximumDecisionLength);
        if (line.Length == 0)
        {
            throw new ArgumentException("An Escalation asks for a decision.", nameof(decision));
        }

        var lines = options.Select(option => OneLine(option, MaximumOptionLength)).ToList();
        if (lines.Any(option => option.Length == 0))
        {
            throw new ArgumentException("An option is not empty.", nameof(options));
        }

        return lines.Count > MaximumOptions
            ? throw new ArgumentException($"An Escalation has at most {MaximumOptions} options.", nameof(options))
            : new EscalationRequest(line, lines);
    }

    internal static string OneLine(string? text, int maximum)
    {
        var line = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return line.Length <= maximum ? line : line[..(maximum - 1)].TrimEnd() + "…";
    }
}

/// <summary>Everything one Escalation changes, committed together (AD-1, AD-5).</summary>
/// <param name="Escalation">The Escalation to record on the Task.</param>
/// <param name="From">The state the Task is in; the move is made only while it still is.</param>
/// <param name="Effects">The intents journaled with the move: the state label, the comment, and the notification.</param>
public sealed record EscalationPlan(Escalation Escalation, TaskState From, IReadOnlyList<NewEffect> Effects)
{
    /// <summary>The state the Task moves to.</summary>
    public TaskState To => TaskState.WaitingForOwner;
}
