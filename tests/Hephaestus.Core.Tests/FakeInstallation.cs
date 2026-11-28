using Hephaestus.Core.Aliases;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Readiness;
using Hephaestus.Core.Trust;

namespace Hephaestus.Core.Tests;

/// <summary>The Installation Configuration of Story 1.9 in memory: the two ports as Persistence implements them, without a database.</summary>
internal sealed class FakeInstallation : IReadStore, IInstallationConfiguration
{
    public Dictionary<string, Alias> Aliases { get; } = [];

    public InstallationLimits Limits { get; set; } = InstallationLimits.None;

    public SortedSet<string> ProtectedPaths { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Settings { get; } = [];

    public Task<AliasWrite> AddAliasAsync(Alias alias, CancellationToken cancellationToken)
    {
        if (Aliases.TryGetValue(alias.Name, out var stored))
        {
            return Task.FromResult(new AliasWrite(stored, Added: false));
        }

        Aliases[alias.Name] = alias;
        return Task.FromResult(new AliasWrite(alias, Added: true));
    }

    public Task<Alias?> RemoveAliasAsync(string name, CancellationToken cancellationToken)
    {
        Aliases.Remove(name, out var removed);
        return Task.FromResult(removed);
    }

    public Task<InstallationLimits> SetLimitsAsync(LimitsChange change, CancellationToken cancellationToken)
    {
        Limits = new InstallationLimits(change.TaskDuration ?? Limits.TaskDuration, change.CpuMillis ?? Limits.CpuMillis, change.MemoryBytes ?? Limits.MemoryBytes);
        return Task.FromResult(Limits);
    }

    public Task<bool> AddProtectedPathAsync(string glob, CancellationToken cancellationToken) => Task.FromResult(ProtectedPaths.Add(glob));

    public Task<bool> RemoveProtectedPathAsync(string glob, CancellationToken cancellationToken) => Task.FromResult(ProtectedPaths.Remove(glob));

    public Task SetSettingAsync(string name, string value, CancellationToken cancellationToken)
    {
        Settings[name] = value;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Alias>> ListAliasesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Alias>>([.. Aliases.Values.OrderBy(a => a.Name, StringComparer.Ordinal)]);

    public Task<InstallationLimits> GetLimitsAsync(CancellationToken cancellationToken) => Task.FromResult(Limits);

    public Task<IReadOnlyList<string>> ListProtectedPathsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([.. ProtectedPaths]);

    public Task<IReadOnlyDictionary<string, string>> ListSettingsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Settings));

    public Task SetGitHubAppIdAsync(long appId, string slug, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<string?> GetGitHubAppSlugAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Hephaestus.Core.Tasks.TaskRecord?> FindTaskAsync(long repositoryId, int issueNumber, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Hephaestus.Core.Tasks.IssueSnapshot?> FindIssueSnapshotAsync(Guid taskId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Hephaestus.Core.Effects.PendingEffect>> ListUnfinishedEffectsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Hephaestus.Core.Effects.JournalEntry>> ListJournalAsync(Guid? taskId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Hephaestus.Core.Escalations.Escalation?> FindEscalationAsync(Guid escalationId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Hephaestus.Core.Escalations.Escalation>> ListEscalationsAsync(Guid taskId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Hephaestus.Core.Tasks.AdmittedComment>> ListAdmittedCommentsAsync(Guid taskId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<TrustedUserWrite> AddTrustedUserAsync(long userId, string login, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> RemoveTrustedUserAsync(long userId, CancellationToken cancellationToken) => throw new NotSupportedException();

    /// <summary>The names of the secrets that are stored, which the tests that need them set.</summary>
    public List<string> SecretNames { get; } = [];

    public Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([.. SecretNames.Order(StringComparer.Ordinal)]);

    public Dictionary<Agent, AgentSetting> AgentSettings { get; } = [];

    public Dictionary<(Agent, AuthenticationMode), AgentCapacity> AgentCapacities { get; } = [];

    public Task SetAgentAuthenticationAsync(Agent agent, AuthenticationMode mode, DateTimeOffset chosenAt, bool credentialStored, CancellationToken cancellationToken)
    {
        AgentSettings[agent] = new AgentSetting(agent, mode, chosenAt);
        if (credentialStored)
        {
            AgentCapacities.Remove((agent, mode));
        }

        return Task.CompletedTask;
    }

    public Task RecordAgentProbeAsync(Agent agent, AuthenticationMode mode, AgentReadiness readiness, string? reasonId, IReadOnlyList<string> reasonArguments, DateTimeOffset probedAt, CancellationToken cancellationToken)
    {
        AgentCapacities[(agent, mode)] = new AgentCapacity(agent, mode, readiness, reasonId, reasonArguments, probedAt, AgentCapacities.GetValueOrDefault((agent, mode))?.ExhaustedUntil);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AgentSetting>> ListAgentSettingsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AgentSetting>>([.. AgentSettings.Values.OrderBy(a => a.Agent)]);

    public Task<IReadOnlyList<AgentCapacity>> ListAgentCapacitiesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AgentCapacity>>([.. AgentCapacities.Values.OrderBy(c => c.Agent).ThenBy(c => c.Mode)]);

    public Task<Hephaestus.Core.Tasks.TaskRecord?> FindTaskAsync(Guid taskId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Hephaestus.Core.Tasks.TaskRecord>> ListTasksAsync(Hephaestus.Core.Tasks.TaskState state, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Hephaestus.Core.Runs.TaskSnapshot?> FindTaskSnapshotAsync(Guid taskId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Hephaestus.Core.Runs.StepRun>> ListStepRunsAsync(Guid taskId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Hephaestus.Core.Runs.StepRun?> FindStepRunAsync(Guid stepRunId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Hephaestus.Core.Windows.Window>> ListWindowsAsync(bool openOnly, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<long?> GetGitHubAppIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<TrustedUser>> ListTrustedUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Dictionary<long, Project> Projects { get; } = [];

    /// <summary>When set, a removal finds the Project already removed, as when two Owners remove it at once.</summary>
    public bool RemovalFindsTheProjectGone { get; set; }

    public Task<ProjectWrite> RegisterProjectAsync(long repositoryId, string fullName, string baseBranch, CancellationToken cancellationToken)
    {
        var registeredAt = DateTimeOffset.UnixEpoch.AddDays(Projects.Count);
        if (!Projects.TryGetValue(repositoryId, out var stored))
        {
            var created = new Project(repositoryId, fullName, baseBranch, registeredAt, null);
            Projects[repositoryId] = created;
            return Task.FromResult(new ProjectWrite(created, ProjectChange.Registered));
        }

        var change = stored.RemovedAt is not null ? ProjectChange.Registered : stored.FullName == fullName && stored.BaseBranch == baseBranch ? ProjectChange.Unchanged : ProjectChange.Updated;
        var updated = stored with { FullName = fullName, BaseBranch = baseBranch, RemovedAt = null };
        Projects[repositoryId] = updated;
        return Task.FromResult(new ProjectWrite(updated, change));
    }

    public Task<Project?> RemoveProjectAsync(long repositoryId, CancellationToken cancellationToken)
    {
        if (RemovalFindsTheProjectGone || !Projects.TryGetValue(repositoryId, out var stored) || stored.RemovedAt is not null)
        {
            return Task.FromResult<Project?>(null);
        }

        var removed = stored with { RemovedAt = DateTimeOffset.UnixEpoch.AddYears(1) };
        Projects[repositoryId] = removed;
        return Task.FromResult<Project?>(removed);
    }

    public Task<IReadOnlyList<Project>> ListProjectsAsync(bool includeRemoved, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Project>>([.. Projects.Values.Where(p => includeRemoved || p.IsActive).OrderBy(p => p.FullName, StringComparer.OrdinalIgnoreCase)]);
}
