namespace Hephaestus.Core.Projects;

/// <summary>
/// A GitHub repository registered with Hephaestus (FR-1). GitHub's numeric repository id is the identity, so a rename changes
/// nothing about which Project this is; <see cref="FullName"/> is for display and is as old as the last time the Owner registered the
/// repository. A removed Project keeps its record, and nothing recorded under it is deleted.
/// </summary>
/// <param name="BaseBranch">The branch that independent Tasks branch from and that approved pull requests are merged into.</param>
/// <param name="RemovedAt">When the Owner removed the Project; null while Hephaestus works on it.</param>
public sealed record Project(long RepositoryId, string FullName, string BaseBranch, DateTimeOffset RegisteredAt, DateTimeOffset? RemovedAt)
{
    public bool IsActive => RemovedAt is null;
}

/// <summary>What a registration did to the stored record.</summary>
public enum ProjectChange
{
    /// <summary>The repository was not registered, or was removed, and is now registered.</summary>
    Registered,

    /// <summary>The Project was registered and its name or its Base Branch changed.</summary>
    Updated,

    /// <summary>The Project was registered exactly so already.</summary>
    Unchanged,
}

/// <summary>A Project as stored, and what this write did to it.</summary>
public readonly record struct ProjectWrite(Project Project, ProjectChange Change);
