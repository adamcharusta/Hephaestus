namespace Hephaestus.Persistence.Model;

/// <summary>
/// An Alias (FR-7): the name the Owner chose, and what it resolves to. Either <see cref="Agent"/> and <see cref="Model"/> are set, or
/// <see cref="Effort"/> is; a check constraint holds the table to that. Agent and Effort are the tags Core writes.
/// </summary>
internal sealed class AliasEntry
{
    public string Name { get; set; } = string.Empty;

    public string? Agent { get; set; }

    public string? Model { get; set; }

    public string? Effort { get; set; }

    public DateTimeOffset AddedAt { get; set; }
}
