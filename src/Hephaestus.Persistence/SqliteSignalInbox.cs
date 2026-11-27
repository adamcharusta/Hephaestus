using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Workspaces;
using Hephaestus.Persistence.Model;
using Microsoft.EntityFrameworkCore;

namespace Hephaestus.Persistence;

/// <summary><see cref="ISignalInbox"/> over the <c>Signals</c> table (AD-1): one transaction per append, and a duplicate is a no-op.</summary>
internal sealed class SqliteSignalInbox(HephaestusDatabase database) : ISignalInbox
{
    public async Task<int> AppendAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken)
    {
        if (signals.Count == 0)
        {
            return 0;
        }

        await using var context = database.OpenWriter();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var appended = 0;
        foreach (var signal in signals)
        {
            var kind = signal.Kind.Tag();
            var occurredAt = UtcInstantConverter.ToText(signal.OccurredAt);
            var stepRun = signal.StepRunId is { } stepRunId ? EntityIdConverter.ToText(stepRunId) : null;
            var cause = signal.Cause?.Tag();
            appended += await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "Signals" ("Source", "SourceEventId", "Kind", "RepositoryId", "IssueNumber", "ActorId", "ActorLogin", "Label", "CommentId", "CommentAuthorId", "OccurredAt", "IssueTitle", "IssueBody", "CommentBody", "StepRunId", "ExitCode", "Cause")
                VALUES ({signal.Source}, {signal.SourceEventId}, {kind}, {signal.RepositoryId}, {signal.IssueNumber}, {signal.ActorId}, {signal.ActorLogin}, {signal.Label}, {signal.CommentId}, {signal.CommentAuthorId}, {occurredAt}, {signal.IssueTitle}, {signal.IssueBody}, {signal.CommentBody}, {stepRun}, {signal.ExitCode}, {cause})
                ON CONFLICT ("Source", "SourceEventId") DO NOTHING
                """,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return appended;
    }

    public async Task<IReadOnlyList<StoredSignal>> ReadAsync(long afterSequence, int limit, CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        var entries = await context.Signals.Where(s => s.Sequence > afterSequence).OrderBy(s => s.Sequence).Take(limit).ToListAsync(cancellationToken);
        return [.. entries.Select(ToStored)];
    }

    public async Task<IReadOnlyList<StoredSignal>> ReadPendingAsync(int limit, CancellationToken cancellationToken)
    {
        var pending = SignalStatus.Pending.Tag();
        await using var context = database.OpenReader();
        var entries = await context.Signals.Where(s => s.Status == pending).OrderBy(s => s.Sequence).Take(limit).ToListAsync(cancellationToken);
        return [.. entries.Select(ToStored)];
    }

    private static StoredSignal ToStored(SignalEntry entry) => new(
        entry.Sequence,
        new Signal(
            entry.Source,
            entry.SourceEventId,
            SignalKinds.TryParse(entry.Kind, out var kind) ? kind : throw new InvalidOperationException($"The stored Signal {entry.Sequence} has the unknown kind '{entry.Kind}'."),
            entry.RepositoryId,
            entry.IssueNumber,
            entry.ActorId,
            entry.ActorLogin,
            entry.Label,
            entry.CommentId,
            entry.CommentAuthorId,
            entry.OccurredAt,
            entry.IssueTitle,
            entry.IssueBody,
            entry.CommentBody,
            entry.StepRunId,
            entry.ExitCode,
            entry.Cause is null ? null : StepExitCauses.TryParse(entry.Cause, out var cause) ? cause : throw new InvalidOperationException($"The stored Signal {entry.Sequence} has the unknown cause '{entry.Cause}'.")),
        SignalStatuses.TryParse(entry.Status, out var status) ? status : throw new InvalidOperationException($"The stored Signal {entry.Sequence} has the unknown status '{entry.Status}'."),
        entry.Reason,
        entry.TaskId);
}
