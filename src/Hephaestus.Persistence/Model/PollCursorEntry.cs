namespace Hephaestus.Persistence.Model;

/// <summary>Where the poller has read up to in one stream of one Project, and the entity tag of that read (AD-1).</summary>
internal sealed class PollCursorEntry
{
    public long RepositoryId { get; set; }

    public string Stream { get; set; } = string.Empty;

    public string Position { get; set; } = string.Empty;

    public string? ETag { get; set; }
}
