namespace Hephaestus.Persistence.Model;

/// <summary>
/// One line of the effect journal (AD-5): append-only, written only by the loop through <c>IUnitOfWork</c>. An effect has one <c>intent</c>,
/// committed with the state change that causes it, any number of <c>retry</c> entries, and at most one <c>result</c>, recorded after the
/// change was made. Every line names the Task, the cause, and the key of the effect (NFR-7).
/// </summary>
internal sealed class EffectJournalEntryEntity
{
    public long Sequence { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Entry { get; set; } = string.Empty;

    public Guid TaskId { get; set; }

    /// <summary>What caused the effect: <c>g&lt;signal-sequence&gt;</c> or, later, <c>s&lt;step-run-sequence&gt;</c>.</summary>
    public string Cause { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public int Ordinal { get; set; }

    /// <summary>For a <c>result</c> entry, how the effect ended.</summary>
    public string? Result { get; set; }

    public string? Reason { get; set; }

    public int Failures { get; set; }

    /// <summary>For a <c>retry</c> entry, when the effect is tried next.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    public DateTimeOffset At { get; set; }
}
