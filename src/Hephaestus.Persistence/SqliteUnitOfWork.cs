using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.Persistence.Model;
using Microsoft.EntityFrameworkCore;

namespace Hephaestus.Persistence;

/// <summary>
/// <see cref="IUnitOfWork"/> (AD-1): one transaction per Signal, in which the Signal leaves <c>pending</c> and the Task it creates is
/// written. Only the orchestrator loop uses it; a test fails when anything else names the port.
/// </summary>
internal sealed class SqliteUnitOfWork(HephaestusDatabase database, TimeProvider time) : IUnitOfWork
{
    public async Task<bool> ApplyAsync(SignalApplication application, CancellationToken cancellationToken)
    {
        if (application.Status == SignalStatus.Pending)
        {
            throw new ArgumentException("A Signal is applied or rejected, never left pending.", nameof(application));
        }

        await using var context = database.OpenWriter();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // The first statement is the write that claims the Signal. A read first would hold a snapshot that a Signal appended meanwhile by
        // the poller makes stale, and SQLite then refuses the upgrade to a write at once, without waiting for the busy timeout.
        var processedAt = UtcInstantConverter.ToText(time.GetUtcNow());
        var status = application.Status.Tag();
        var taskId = application.NewTask?.Task.Id ?? application.TaskId;
        var taskIdText = taskId is { } id ? EntityIdConverter.ToText(id) : null;
        var pending = SignalStatus.Pending.Tag();
        var claimed = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "Signals" SET "Status" = {status}, "Reason" = {application.Reason}, "TaskId" = {taskIdText}, "ProcessedAt" = {processedAt}
            WHERE "Sequence" = {application.Sequence} AND "Status" = {pending}
            """,
            cancellationToken);
        if (claimed == 0)
        {
            return false;
        }

        if (application.NewTask is { } created)
        {
            context.Tasks.Add(new TaskEntry
            {
                Id = created.Task.Id,
                RepositoryId = created.Task.RepositoryId,
                IssueNumber = created.Task.IssueNumber,
                State = Core.Tasks.TaskStates.Tag(created.Task.State),
                Branch = created.Task.Branch,
                CreatedAt = created.Task.CreatedAt,
                UpdatedAt = created.Task.UpdatedAt,
            });
            context.IssueSnapshots.Add(new IssueSnapshotEntry
            {
                TaskId = created.Task.Id,
                Title = created.Snapshot.Title,
                Body = created.Snapshot.Body,
                Hash = created.Snapshot.Hash,
                SignalSequence = created.Snapshot.SignalSequence,
                TakenAt = created.Snapshot.TakenAt,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
