namespace Hephaestus.Persistence.Model;

/// <summary>
/// A Signal in the persisted inbox (AD-1). The sequence the database assigns is the inbox order; the source and the source event id
/// are unique together, so a second append of the same event is refused.
/// </summary>
internal sealed class SignalEntry
{
    public long Sequence { get; set; }

    public string Source { get; set; } = string.Empty;

    public string SourceEventId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public long RepositoryId { get; set; }

    public int IssueNumber { get; set; }

    public long? ActorId { get; set; }

    public string? ActorLogin { get; set; }

    public string? Label { get; set; }

    public long? CommentId { get; set; }

    public long? CommentAuthorId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>The issue's title at the time the event was read; only a labelled event has one.</summary>
    public string? IssueTitle { get; set; }

    public string? IssueBody { get; set; }

    /// <summary>The comment's text at the time the event was read; only a created comment has one.</summary>
    public string? CommentBody { get; set; }

    /// <summary><c>pending</c> until the loop applies or rejects the Signal, which it does in one transaction with the Task writes it causes.</summary>
    public string Status { get; set; } = "pending";

    public string? Reason { get; set; }

    public Guid? TaskId { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }
}
