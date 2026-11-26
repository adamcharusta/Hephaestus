namespace Hephaestus.Persistence.Model;

/// <summary>
/// A Project (FR-1). GitHub's numeric repository id is the key and the identity; the name is for display and is refreshed when the
/// Owner registers the repository again. A removed Project keeps its row and has <see cref="RemovedAt"/> set.
/// </summary>
internal sealed class ProjectEntry
{
    public long RepositoryId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string BaseBranch { get; set; } = string.Empty;

    public DateTimeOffset RegisteredAt { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }
}
