using Hephaestus.Core.Aliases;
using System.Text.Json;
using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Trust;
using Hephaestus.Persistence.Model;
using Microsoft.EntityFrameworkCore;

namespace Hephaestus.Persistence;

/// <summary><see cref="IReadStore"/> on a read-only SQLite connection (AD-17).</summary>
internal sealed class SqliteReadStore(HephaestusDatabase database) : IReadStore
{
    public async Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        return await context.SecretNames.OrderBy(s => s.Name).Select(s => s.Name).ToListAsync(cancellationToken);
    }

    public async Task<long?> GetGitHubAppIdAsync(CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        return await context.GitHubAppConnections.Select(c => (long?)c.AppId).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> GetGitHubAppSlugAsync(CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        return await context.GitHubAppConnections.Select(c => c.AppSlug).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TrustedUser>> ListTrustedUsersAsync(CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        var entries = await context.TrustedUsers.ToListAsync(cancellationToken);

        // The case-insensitive order is the Owner's, not SQLite's binary one.
        return [.. entries
            .OrderBy(u => u.Login, StringComparer.OrdinalIgnoreCase).ThenBy(u => u.Login, StringComparer.Ordinal).ThenBy(u => u.UserId)
            .Select(u => new TrustedUser(u.UserId, u.Login, u.AddedAt))];
    }

    public async Task<IReadOnlyList<Alias>> ListAliasesAsync(CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        var entries = await context.Aliases.OrderBy(a => a.Name).ToListAsync(cancellationToken);
        return [.. entries.Select(ToAlias)];
    }

    public async Task<InstallationLimits> GetLimitsAsync(CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        var stored = await context.InstallationLimits.FirstOrDefaultAsync(cancellationToken);
        return stored is null ? InstallationLimits.None : ToLimits(stored);
    }

    public async Task<IReadOnlyList<string>> ListProtectedPathsAsync(CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        return await context.ProtectedPaths.OrderBy(p => p.Pattern).Select(p => p.Pattern).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>> ListSettingsAsync(CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        return await context.Settings.ToDictionaryAsync(s => s.Name, s => s.Value, cancellationToken);
    }

    public async Task<IReadOnlyList<Project>> ListProjectsAsync(bool includeRemoved, CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        var entries = await context.Projects.Where(p => includeRemoved || p.RemovedAt == null).ToListAsync(cancellationToken);

        // The case-insensitive order is the Owner's, not SQLite's binary one.
        return [.. entries
            .OrderBy(p => p.FullName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.FullName, StringComparer.Ordinal).ThenBy(p => p.RepositoryId)
            .Select(ToProject)];
    }

    public async Task<TaskRecord?> FindTaskAsync(long repositoryId, int issueNumber, CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        var entry = await context.Tasks.FirstOrDefaultAsync(t => t.RepositoryId == repositoryId && t.IssueNumber == issueNumber, cancellationToken);
        return entry is null ? null : ToTask(entry);
    }

    public async Task<IssueSnapshot?> FindIssueSnapshotAsync(Guid taskId, CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        var entry = await context.IssueSnapshots.Where(s => s.TaskId == taskId).OrderByDescending(s => s.Id).FirstOrDefaultAsync(cancellationToken);
        return entry is null ? null : new IssueSnapshot(entry.Title, entry.Body, entry.Hash, entry.SignalSequence, entry.TakenAt);
    }

    public async Task<Escalation?> FindEscalationAsync(Guid escalationId, CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        var entry = await context.Escalations.FirstOrDefaultAsync(e => e.Id == escalationId, cancellationToken);
        return entry is null ? null : ToEscalation(entry);
    }

    public async Task<IReadOnlyList<Escalation>> ListEscalationsAsync(Guid taskId, CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        var entries = await context.Escalations.Where(e => e.TaskId == taskId).ToListAsync(cancellationToken);
        return [.. entries.OrderBy(e => e.RaisedAt).ThenBy(e => e.Id).Select(ToEscalation)];
    }

    public async Task<IReadOnlyList<PendingEffect>> ListUnfinishedEffectsAsync(CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        var intent = JournalEntryKind.Intent.Tag();
        var retry = JournalEntryKind.Retry.Tag();
        var result = JournalEntryKind.Result.Tag();
        var intents = await context.EffectJournal
            .Where(e => e.Entry == intent && !context.EffectJournal.Any(r => r.Key == e.Key && r.Entry == result))
            .OrderBy(e => e.Sequence)
            .ToListAsync(cancellationToken);
        if (intents.Count == 0)
        {
            return [];
        }

        var keys = intents.Select(e => e.Key).ToList();
        var taskIds = intents.Where(e => e.TaskId is not null).Select(e => e.TaskId!.Value).Distinct().ToList();
        var retries = (await context.EffectJournal.Where(e => e.Entry == retry && keys.Contains(e.Key)).OrderBy(e => e.Sequence).ToListAsync(cancellationToken))
            .GroupBy(e => e.Key)
            .ToDictionary(group => group.Key, group => group.Last());
        var tasks = (await context.Tasks.Where(t => taskIds.Contains(t.Id)).ToListAsync(cancellationToken)).ToDictionary(t => t.Id);

        // A journal line whose Task is missing cannot be: the intent is written in the transaction that writes the Task. One that is for no
        // Task, a comment on an issue that is none, has none to find.
        return [.. intents.Select(entry => new PendingEffect(
            ToEffect(entry),
            entry.TaskId is { } taskId ? ToTask(tasks[taskId]) : null,
            retries.TryGetValue(entry.Key, out var last) ? last.Failures : 0,
            retries.TryGetValue(entry.Key, out last) ? last.NextAttemptAt : null))];
    }

    public async Task<IReadOnlyList<JournalEntry>> ListJournalAsync(Guid? taskId, CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        var entries = await context.EffectJournal.Where(e => taskId == null || e.TaskId == taskId).OrderBy(e => e.Sequence).ToListAsync(cancellationToken);
        return [.. entries.Select(entry => new JournalEntry(
            entry.Sequence,
            JournalEntryKinds.TryParse(entry.Entry, out var kind) ? kind : throw new InvalidOperationException($"The journal line {entry.Sequence} has the unknown entry '{entry.Entry}'."),
            ToEffect(entry),
            EffectResults.TryParse(entry.Result, out var result) ? result : null,
            entry.Reason,
            entry.Failures,
            entry.NextAttemptAt,
            entry.At))];
    }

    private static Effect ToEffect(EffectJournalEntryEntity entry) => new(
        entry.Key,
        entry.TaskId,
        entry.Cause,
        EffectKinds.TryParse(entry.Kind, out var kind) ? kind : throw new InvalidOperationException($"The journal line {entry.Sequence} has the unknown effect kind '{entry.Kind}'."),
        entry.Detail,
        entry.Ordinal,
        entry.RepositoryId,
        entry.IssueNumber);

    public async Task<IReadOnlyList<AdmittedComment>> ListAdmittedCommentsAsync(Guid taskId, CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        var entries = await context.AdmittedComments.Where(c => c.TaskId == taskId).OrderBy(c => c.SignalSequence).ThenBy(c => c.Id).ToListAsync(cancellationToken);
        return [.. entries.Select(entry => new AdmittedComment(entry.TaskId, entry.CommentId, entry.AuthorId, entry.AuthorLogin, entry.Body, entry.Hash, entry.SignalSequence, entry.AdmittedAt, entry.AnswersEscalationId))];
    }

    private static Escalation ToEscalation(EscalationEntry entry) => new(
        entry.Id,
        entry.TaskId,
        EscalationKinds.TryParse(entry.Kind, out var kind) ? kind : throw new InvalidOperationException($"The stored Escalation {entry.Id} has the unknown kind '{entry.Kind}'."),
        entry.Decision,
        JsonSerializer.Deserialize<string[]>(entry.Options) ?? [],
        entry.ResumeCondition,
        entry.Cause,
        entry.RaisedAt,
        entry.EffectKey is { } key && EffectKinds.TryParse(entry.EffectKind, out var effectKind) ? new FailedEffect(key, effectKind, entry.EffectDetail ?? string.Empty, entry.EffectReason ?? string.Empty) : null,
        entry.ClosedAt,
        EscalationOutcomes.TryParse(entry.Outcome, out var outcome) ? outcome : null);

    internal static TaskRecord ToTask(TaskEntry entry) => new(
        entry.Id,
        entry.RepositoryId,
        entry.IssueNumber,
        TaskStates.TryParse(entry.State, out var state) ? state : throw new InvalidOperationException($"The stored Task {entry.Id} has the unknown state '{entry.State}'."),
        entry.Branch,
        entry.CreatedAt,
        entry.UpdatedAt);

    internal static Project ToProject(ProjectEntry entry) => new(entry.RepositoryId, entry.FullName, entry.BaseBranch, entry.RegisteredAt, entry.RemovedAt);

    internal static InstallationLimits ToLimits(InstallationLimitsEntry entry) =>
        new(entry.TaskDurationSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null, entry.CpuMillis, entry.MemoryBytes);

    internal static Alias ToAlias(AliasEntry entry) =>
        Agents.TryParse(entry.Agent, out var agent) && entry.Model is { } model
            ? Alias.ForModel(entry.Name, agent, model, entry.AddedAt)
            : Alias.ForEffort(entry.Name, Efforts.TryParse(entry.Effort, out var effort) ? effort : throw new InvalidOperationException($"The stored Alias '{entry.Name}' names no known Agent or Effort."), entry.AddedAt);
}
