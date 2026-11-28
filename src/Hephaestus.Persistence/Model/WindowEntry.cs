namespace Hephaestus.Persistence.Model;

/// <summary>
/// A window of a Project (FR-16, AD-1): opened by a Signal the loop applied, closed by one or by the loop when the Project has nothing left to work. A Project has at most one open, which the database refuses
/// a second of.
/// </summary>
internal sealed class WindowEntry
{
    public Guid Id { get; set; }

    public long RepositoryId { get; set; }

    public DateTimeOffset OpenedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Why it closed, as one of the names of <c>WindowEnd</c>.</summary>
    public string? End { get; set; }
}
