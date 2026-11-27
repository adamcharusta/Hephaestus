namespace Hephaestus.Persistence.Model;

/// <summary>A Task: one Project and one issue number for life (AD-2). Only the loop writes it, through <c>IUnitOfWork</c> (AD-1).</summary>
internal sealed class TaskEntry
{
    public Guid Id { get; set; }

    public long RepositoryId { get; set; }

    public int IssueNumber { get; set; }

    public string State { get; set; } = string.Empty;

    public string Branch { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>The issue's title and body as a Marker Label event vouched for them, with the hash a later comparison uses (AD-15).</summary>
internal sealed class IssueSnapshotEntry
{
    public long Id { get; set; }

    public Guid TaskId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;

    public long SignalSequence { get; set; }

    public DateTimeOffset TakenAt { get; set; }
}

/// <summary>
/// A comment of a Trusted User on a Task's issue, as the loop admitted it (AD-15), with the hash a later comparison uses. A comment that answered
/// an Escalation names it. Only the loop writes it, in the transaction that applies the Signal.
/// </summary>
internal sealed class AdmittedCommentEntry
{
    public long Id { get; set; }

    public Guid TaskId { get; set; }

    public long CommentId { get; set; }

    public long AuthorId { get; set; }

    public string? AuthorLogin { get; set; }

    public string Body { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;

    public long SignalSequence { get; set; }

    public DateTimeOffset AdmittedAt { get; set; }

    public Guid? AnswersEscalationId { get; set; }
}
