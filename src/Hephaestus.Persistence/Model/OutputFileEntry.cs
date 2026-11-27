namespace Hephaestus.Persistence.Model;

/// <summary>
/// The index of one stored output file (FR-53): one row for each container run, keyed by its step run. The file itself lives under the data
/// directory; the row says whose it is, where, and how much of it was written.
/// </summary>
internal sealed class OutputFileEntry
{
    public Guid StepRunId { get; set; }

    public Guid TaskId { get; set; }

    public string ContainerId { get; set; } = string.Empty;

    /// <summary>The path of the file relative to the output directory, with forward slashes.</summary>
    public string Path { get; set; } = string.Empty;

    public long Bytes { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When the file was completed; null while the run is writing it, or when the writer was disposed before the run ended.</summary>
    public DateTimeOffset? CompletedAt { get; set; }
}
