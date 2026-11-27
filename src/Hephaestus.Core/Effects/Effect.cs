using System.Globalization;
using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Effects;

/// <summary>What an effect does outside the process (AD-5). Comments, pull requests, and the rest join this set with the stories that cause them.</summary>
public enum EffectKind
{
    /// <summary>Makes the labels of the Task's issue hold the state label of the Task's state, and no other state label.</summary>
    Label,

    /// <summary>Posts one comment on an issue, whose last line is the hidden marker of the effect's key, unless a comment with that marker is there already.</summary>
    Comment,
}

public static class EffectKinds
{
    /// <summary>The name stored, shown, and written in an idempotency key; never translated.</summary>
    public static string Tag(this EffectKind kind) => kind switch
    {
        EffectKind.Label => "label",
        EffectKind.Comment => "comment",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static bool TryParse(string? tag, out EffectKind kind)
    {
        foreach (var candidate in Enum.GetValues<EffectKind>())
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

/// <summary>How an effect ended, as the journal records it; each is its own entry, recorded after the far side was asked.</summary>
public enum EffectResult
{
    /// <summary>The change was made.</summary>
    Applied,

    /// <summary>The far side already was as the effect wants it, found by reading it, so nothing was changed (recovery, or a change made by hand and made back).</summary>
    AlreadyInPlace,

    /// <summary>The Task left the state the effect was for before the effect was made; a later effect shows the new state, so nothing was changed.</summary>
    Superseded,

    /// <summary>The far side stayed unreachable until the bound of retries; the effect is not tried again and the Task waits for the Owner.</summary>
    GaveUp,

    /// <summary>The far side refused for good, for example because the issue was deleted; the effect is not tried again and the Task waits for the Owner.</summary>
    Refused,
}

public static class EffectResults
{
    public static string Tag(this EffectResult result) => result switch
    {
        EffectResult.Applied => "applied",
        EffectResult.AlreadyInPlace => "already-in-place",
        EffectResult.Superseded => "superseded",
        EffectResult.GaveUp => "gave-up",
        EffectResult.Refused => "refused",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
    };

    public static bool TryParse(string? tag, out EffectResult result)
    {
        foreach (var candidate in Enum.GetValues<EffectResult>())
        {
            if (candidate.Tag() == tag)
            {
                result = candidate;
                return true;
            }
        }

        result = default;
        return false;
    }
}

/// <summary>Why an attempt failed or an effect was refused; machine values, never translated.</summary>
public static class EffectReasons
{
    public const string Unreachable = "unreachable";

    public const string RateLimited = "rate-limited";

    public const string NotConnected = "not-connected";

    public const string KeyUnreadable = "key-unreadable";

    public const string IssueNotFound = "issue-not-found";

    public const string AppNotInstalled = "app-not-installed";

    public const string Rejected = "rejected";

    /// <summary>The journal holds an intent that this version does not know how to make.</summary>
    public const string UnknownEffect = "unknown-effect";
}

/// <summary>The idempotency key of an effect (AD-5): <c>&lt;scope&gt;:&lt;cause&gt;:&lt;effect-kind&gt;:&lt;ordinal&gt;</c>.</summary>
public static class EffectKeys
{
    /// <summary>The key of the <paramref name="ordinal"/>th effect of a kind that a cause had for a Task; the scope is the Task's id.</summary>
    public static string For(Guid taskId, string cause, EffectKind kind, int ordinal) => For(Scope(taskId, repositoryId: null), cause, kind, ordinal);

    /// <summary>The key of the <paramref name="ordinal"/>th effect of a kind that a cause had in a scope, see <see cref="Scope"/>.</summary>
    public static string For(string scope, string cause, EffectKind kind, int ordinal) =>
        string.Join(':', scope, cause, kind.Tag(), ordinal.ToString(CultureInfo.InvariantCulture));

    /// <summary>The scope of an effect: the Task's id, or <c>project:&lt;repository-id&gt;</c> for an effect on an issue that is no Task.</summary>
    public static string Scope(Guid? taskId, long? repositoryId) =>
        taskId is { } id
            ? id.ToString("D")
            : "project:" + (repositoryId ?? throw new ArgumentException("An effect that is for no Task is for a Project.", nameof(repositoryId))).ToString(CultureInfo.InvariantCulture);

    /// <summary>The cause of what a Signal makes happen: <c>g&lt;signal-sequence&gt;</c>.</summary>
    public static string SignalCause(long sequence) => "g" + sequence.ToString(CultureInfo.InvariantCulture);
}

/// <summary>An effect to be journaled together with the state change that causes it.</summary>
/// <param name="TaskId">The Task it is for; null for an effect on an issue that is no Task, which is for a Project and names the issue.</param>
/// <param name="Cause">What caused it: <see cref="EffectKeys.SignalCause"/> of the Signal, or the cause of the effect whose failure led to this one.</param>
/// <param name="Detail">What the effect is for: for a label, the tag of the state it shows; for a comment, see <see cref="IssueNotices"/>.</param>
/// <param name="RepositoryId">The repository of the issue, for an effect that is for no Task, and for a comment.</param>
/// <param name="IssueNumber">The issue a comment is for.</param>
/// <param name="NoticeKey">What makes a comment the same one again: once an intent with this key was journaled for the issue, no other is; null for an effect that may repeat.</param>
public sealed record NewEffect(Guid? TaskId, string Cause, EffectKind Kind, string Detail, long? RepositoryId = null, int? IssueNumber = null, string? NoticeKey = null)
{
    /// <summary>The scope of the key: the Task, or the Project when the issue is no Task.</summary>
    public string Scope => EffectKeys.Scope(TaskId, RepositoryId);
}

/// <summary>An effect as the journal holds its intent.</summary>
/// <param name="TaskId">The Task it is for; null for an effect on an issue that is no Task, whose repository and issue are named.</param>
/// <param name="Ordinal">How many effects of this kind the cause had in the scope before this one.</param>
/// <param name="RepositoryId">The repository of the issue of a comment.</param>
/// <param name="IssueNumber">The issue of a comment.</param>
public sealed record Effect(string Key, Guid? TaskId, string Cause, EffectKind Kind, string Detail, int Ordinal, long? RepositoryId = null, int? IssueNumber = null);

/// <summary>An effect with an intent and no result yet, with the Task it is for (none for an effect on an issue that is no Task) and how its attempts went.</summary>
/// <param name="Failures">How many attempts failed so far; a wait for a rate limit is not one.</param>
/// <param name="NextAttemptAt">When it is to be tried next; null when it has not been tried or is due now.</param>
public sealed record PendingEffect(Effect Effect, TaskRecord? Task, int Failures, DateTimeOffset? NextAttemptAt);

/// <summary>What the loop records after it has asked the far side: a result that ends the effect, or a retry that schedules the next attempt.</summary>
/// <param name="Result">How the effect ended; null for a retry.</param>
/// <param name="Reason">Why, for a refusal, a give-up, or a retry.</param>
/// <param name="MoveTaskTo">The state the Task moves to in the same transaction, when the effect could not be completed.</param>
/// <param name="NewEffects">The effects that the move causes, journaled with it.</param>
public sealed record EffectRecording(
    Effect Effect,
    EffectResult? Result,
    string? Reason,
    int Failures,
    DateTimeOffset? NextAttemptAt,
    TaskState? MoveTaskTo = null,
    IReadOnlyList<NewEffect>? NewEffects = null);

/// <summary>The kinds of entry the journal holds; it is append-only, and an effect has one intent, any retries, and at most one result.</summary>
public enum JournalEntryKind
{
    Intent,
    Retry,
    Result,
}

public static class JournalEntryKinds
{
    public static string Tag(this JournalEntryKind kind) => kind switch
    {
        JournalEntryKind.Intent => "intent",
        JournalEntryKind.Retry => "retry",
        JournalEntryKind.Result => "result",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static bool TryParse(string? tag, out JournalEntryKind kind)
    {
        foreach (var candidate in Enum.GetValues<JournalEntryKind>())
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

/// <summary>One line of the effect journal: what the entry is, the effect, and what caused it, which every entry names (NFR-7).</summary>
public sealed record JournalEntry(
    long Sequence,
    JournalEntryKind Entry,
    Effect Effect,
    EffectResult? Result,
    string? Reason,
    int Failures,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset At);

/// <summary>The waits between the attempts at an effect, which are bounded (NFR-12).</summary>
public static class EffectRetries
{
    /// <summary>The wait after the first failed attempt; each further failure doubles it.</summary>
    public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(30);

    /// <summary>The longest wait between two attempts.</summary>
    public static readonly TimeSpan LongestDelay = TimeSpan.FromMinutes(10);

    /// <summary>The wait after the <paramref name="failures"/>th failed attempt in a row.</summary>
    public static TimeSpan DelayAfter(int failures)
    {
        var doublings = Math.Clamp(failures - 1, 0, 16);
        var delay = TimeSpan.FromTicks(FirstDelay.Ticks << doublings);
        return delay > LongestDelay ? LongestDelay : delay;
    }
}
