namespace Hephaestus.Persistence.Model;

/// <summary>
/// An Escalation recorded on a Task (FR-48): the question Hephaestus asked the Owner, with the condition that ends the wait. Only the loop
/// writes it, through <c>IUnitOfWork</c>, in the transaction that moves the Task to <c>waiting-for-owner</c>. A Task has at most one that is
/// open, because one Escalation asks for one decision.
/// </summary>
internal sealed class EscalationEntry
{
    public Guid Id { get; set; }

    public Guid TaskId { get; set; }

    public string Kind { get; set; } = string.Empty;

    /// <summary>The decision as the caller wrote it; empty for an effect that could not be completed.</summary>
    public string Decision { get; set; } = string.Empty;

    /// <summary>The options, a JSON array of strings; empty for an effect that could not be completed.</summary>
    public string Options { get; set; } = "[]";

    public string ResumeCondition { get; set; } = string.Empty;

    public string Cause { get; set; } = string.Empty;

    public DateTimeOffset RaisedAt { get; set; }

    /// <summary>For an effect that could not be completed: the key of the effect.</summary>
    public string? EffectKey { get; set; }

    public string? EffectKind { get; set; }

    public string? EffectDetail { get; set; }

    public string? EffectReason { get; set; }

    /// <summary>For work that was not pushed: the tag of the check that failed.</summary>
    public string? Check { get; set; }

    /// <summary>When the Task left <c>waiting-for-owner</c> without an answer; null while the Escalation is open.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    public string? Outcome { get; set; }
}
