namespace Hephaestus.Core.Ports;

/// <summary>Where the poller has read up to in one stream of a Project, and the validator GitHub gave for that read.</summary>
/// <param name="Position">The stream's own marker: the newest event id read, or the newest update time read.</param>
/// <param name="ETag">The entity tag of the first page when it was read, which the next request sends to ask whether anything changed; null when GitHub gave none.</param>
public sealed record PollCursor(string Position, string? ETag);

/// <summary>The streams the poller keeps a cursor for; the names are stored and never translated.</summary>
public static class PollStreams
{
    /// <summary>The repository's issue events, labelled, unlabelled, closed, and reopened; <see cref="PollCursor.Position"/> is the newest event id.</summary>
    public const string IssueEvents = "issue-events";

    /// <summary>The repository's issue comments, created or edited; <see cref="PollCursor.Position"/> is the newest update time, in ISO 8601 UTC.</summary>
    public const string Comments = "comments";

    /// <summary>
    /// When Hephaestus began to watch the Project, as an ISO 8601 UTC time, so that a stream with no cursor yet reads from then and
    /// not from the beginning. Registering a Project writes it; removing the Project removes every cursor.
    /// </summary>
    public const string Since = "since";
}

/// <summary>
/// The poller's cursors (AD-1, AD-4), one per Project and stream. The poller appends first and advances a cursor second, so a crash
/// between the two re-reads events that the inbox then refuses as duplicates, and no event is lost.
/// </summary>
public interface IPollCursorStore
{
    /// <summary>The cursor of the stream, or null when it has none.</summary>
    Task<PollCursor?> GetAsync(long repositoryId, string stream, CancellationToken cancellationToken);

    /// <summary>Stores the cursor of the stream, replacing an earlier one.</summary>
    Task SetAsync(long repositoryId, string stream, PollCursor cursor, CancellationToken cancellationToken);
}
