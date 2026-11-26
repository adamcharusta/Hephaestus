namespace Hephaestus.Persistence.Model;

/// <summary>The installation limits (NFR-5), one row, <see cref="TheOnlyId"/>. A column is null until the Owner sets that limit.</summary>
internal sealed class InstallationLimitsEntry
{
    public const int TheOnlyId = 1;

    public int Id { get; set; } = TheOnlyId;

    public long? TaskDurationSeconds { get; set; }

    public int? CpuMillis { get; set; }

    public long? MemoryBytes { get; set; }
}
