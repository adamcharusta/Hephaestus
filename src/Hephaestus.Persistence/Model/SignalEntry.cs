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
}
