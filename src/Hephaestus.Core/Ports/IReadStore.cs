using Hephaestus.Core.Aliases;
using Hephaestus.Core.Effects;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Trust;

namespace Hephaestus.Core.Ports;

/// <summary>
/// Every read of stored data goes through here (AD-17). Persistence implements it on a read-only SQLite connection, so a read
/// can never change anything and no endpoint depends on <c>IUnitOfWork</c>.
/// </summary>
public interface IReadStore
{
    /// <summary>The names of the stored secrets in ascending order, and nothing else about them.</summary>
    Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken cancellationToken);

    /// <summary>The id of Hephaestus's own GitHub App, or null when none was connected.</summary>
    Task<long?> GetGitHubAppIdAsync(CancellationToken cancellationToken);

    /// <summary>The slug of Hephaestus's own GitHub App, from which its bot account's login follows; null when none was connected, or when it was connected before the slug was kept.</summary>
    Task<string?> GetGitHubAppSlugAsync(CancellationToken cancellationToken);

    /// <summary>The Trusted Users, ascending by login and then by id.</summary>
    Task<IReadOnlyList<TrustedUser>> ListTrustedUsersAsync(CancellationToken cancellationToken);

    /// <summary>The Aliases, ascending by name.</summary>
    Task<IReadOnlyList<Alias>> ListAliasesAsync(CancellationToken cancellationToken);

    /// <summary>The installation limits; a limit nobody set is null.</summary>
    Task<InstallationLimits> GetLimitsAsync(CancellationToken cancellationToken);

    /// <summary>The protected paths, ascending.</summary>
    Task<IReadOnlyList<string>> ListProtectedPathsAsync(CancellationToken cancellationToken);

    /// <summary>The settings the Owner has set, by name; a setting never set is absent and has its default.</summary>
    Task<IReadOnlyDictionary<string, string>> ListSettingsAsync(CancellationToken cancellationToken);

    /// <summary>The Projects, ascending by name and then by repository id: those Hephaestus works on, and the removed ones too when asked.</summary>
    Task<IReadOnlyList<Project>> ListProjectsAsync(bool includeRemoved, CancellationToken cancellationToken);

    /// <summary>The Task of the issue of the Project, or null when the issue is not a Task.</summary>
    Task<TaskRecord?> FindTaskAsync(long repositoryId, int issueNumber, CancellationToken cancellationToken);

    /// <summary>The newest snapshot of the Task's issue, or null when it has none.</summary>
    Task<IssueSnapshot?> FindIssueSnapshotAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>The effects that have an intent and no result (AD-5), in the order they were journaled, with the Task each is for and how its attempts went.</summary>
    Task<IReadOnlyList<PendingEffect>> ListUnfinishedEffectsAsync(CancellationToken cancellationToken);

    /// <summary>The effect journal, in the order it was written: all of it, or only what concerns one Task.</summary>
    Task<IReadOnlyList<JournalEntry>> ListJournalAsync(Guid? taskId, CancellationToken cancellationToken);
}
