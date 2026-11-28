using Hephaestus.Core.Aliases;
using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Readiness;
using Hephaestus.Core.Runs;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Trust;
using Hephaestus.Core.Windows;

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

    /// <summary>The Escalation with the id, or null when there is none.</summary>
    Task<Escalation?> FindEscalationAsync(Guid escalationId, CancellationToken cancellationToken);

    /// <summary>The Escalations of a Task in the order they were raised; the open one, if there is one, is the last.</summary>
    Task<IReadOnlyList<Escalation>> ListEscalationsAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>The comments Trusted Users wrote on the Task's issue that were admitted, oldest first; one that answered an Escalation says which.</summary>
    Task<IReadOnlyList<AdmittedComment>> ListAdmittedCommentsAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>The effects that have an intent and no result (AD-5), in the order they were journaled, with the Task each is for and how its attempts went.</summary>
    Task<IReadOnlyList<PendingEffect>> ListUnfinishedEffectsAsync(CancellationToken cancellationToken);

    /// <summary>The effect journal, in the order it was written: all of it, or only what concerns one Task.</summary>
    Task<IReadOnlyList<JournalEntry>> ListJournalAsync(Guid? taskId, CancellationToken cancellationToken);

    /// <summary>How each Agent signs in, as far as the Owner has chosen, ascending by Agent.</summary>
    Task<IReadOnlyList<AgentSetting>> ListAgentSettingsAsync(CancellationToken cancellationToken);

    /// <summary>The capacity records of the Agent credentials that were probed, ascending by Agent and then mode (AD-20).</summary>
    Task<IReadOnlyList<AgentCapacity>> ListAgentCapacitiesAsync(CancellationToken cancellationToken);

    /// <summary>The Task with the id, or null when there is none.</summary>
    Task<TaskRecord?> FindTaskAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>The Tasks that are in the state, by issue number and then repository id.</summary>
    Task<IReadOnlyList<TaskRecord>> ListTasksAsync(TaskState state, CancellationToken cancellationToken);

    /// <summary>What the Task started with, or null when it has not started (FR-2).</summary>
    Task<TaskSnapshot?> FindTaskSnapshotAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>The step runs of the Task, in the order they were planned.</summary>
    Task<IReadOnlyList<StepRun>> ListStepRunsAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>The step run with the id, or null when there is none.</summary>
    Task<StepRun?> FindStepRunAsync(Guid stepRunId, CancellationToken cancellationToken);

    /// <summary>The windows of the Projects, oldest first: the open ones only, or all of them.</summary>
    Task<IReadOnlyList<Window>> ListWindowsAsync(bool openOnly, CancellationToken cancellationToken);
}
