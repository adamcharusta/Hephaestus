using System.Text.Json;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.Failures;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Runs;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Windows;
using Hephaestus.Core.Workspaces;
using Hephaestus.Persistence.Model;
using Microsoft.EntityFrameworkCore;

namespace Hephaestus.Persistence;

/// <summary>
/// <see cref="IUnitOfWork"/> (AD-1): one transaction per Signal, in which the Signal leaves <c>pending</c>, the Task it creates is
/// written, and the intents of the effects it causes are journaled (AD-5); and one transaction for each result of an effect. Only the
/// orchestrator loop uses it; a test fails when anything else names the port.
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

        if (application.MoveTaskTo is { } to && taskIdText is not null)
        {
            var state = Core.Tasks.TaskStates.Tag(to);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Tasks" SET "State" = {state}, "UpdatedAt" = {processedAt} WHERE "Id" = {taskIdText}""",
                cancellationToken);
        }

        if (application.EndEscalation is { } outcome && taskIdText is not null)
        {
            var ended = outcome.Tag();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Escalations" SET "ClosedAt" = {processedAt}, "Outcome" = {ended} WHERE "TaskId" = {taskIdText} AND "ClosedAt" IS NULL""",
                cancellationToken);
        }

        if (application.AdmitComment is { } admitted)
        {
            context.AdmittedComments.Add(new AdmittedCommentEntry
            {
                TaskId = admitted.TaskId,
                CommentId = admitted.CommentId,
                AuthorId = admitted.AuthorId,
                AuthorLogin = admitted.AuthorLogin,
                Body = admitted.Body,
                Hash = admitted.Hash,
                SignalSequence = admitted.SignalSequence,
                AdmittedAt = admitted.AdmittedAt,
                AnswersEscalationId = admitted.AnswersEscalationId,
            });
        }

        if (application.OpenWindow is { } opened)
        {
            context.Windows.Add(new WindowEntry { Id = opened.Id, RepositoryId = opened.RepositoryId, OpenedAt = opened.OpenedAt });
        }

        if (application.CloseWindow is { } closing)
        {
            await CloseWindowAsync(context, closing, processedAt, cancellationToken);
        }

        if (application.StepRun is { } stepRun)
        {
            // A change that is not made, because the run has gone further already, leaves the Signal applied all the same: the runtime reports a container's end once, and what it reported is not lost.
            await WriteStepRunChangeAsync(context, stepRun, processedAt, cancellationToken);
        }

        if (application.NewEffects is { Count: > 0 } effects)
        {
            await AppendIntentsAsync(context, effects, processedAt, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DispatchAsync(TaskDispatch dispatch, CancellationToken cancellationToken)
    {
        await using var context = database.OpenWriter();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var at = UtcInstantConverter.ToText(time.GetUtcNow());
        var taskId = EntityIdConverter.ToText(dispatch.TaskId);
        var running = Core.Tasks.TaskStates.Tag(Core.Tasks.TaskState.Running);
        var queued = Core.Tasks.TaskStates.Tag(Core.Tasks.TaskState.Queued);

        // The only edge into Running, and the rule that two Tasks never run: the move is made only while the Task is Queued and no Task is Running.
        var moved = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "Tasks" SET "State" = {running}, "UpdatedAt" = {at}
            WHERE "Id" = {taskId} AND "State" = {queued} AND NOT EXISTS (SELECT 1 FROM "Tasks" WHERE "State" = {running})
            """,
            cancellationToken);
        if (moved == 0)
        {
            return false;
        }

        if (dispatch.Snapshot is { } snapshot)
        {
            context.TaskSnapshots.Add(new TaskSnapshotEntry
            {
                TaskId = snapshot.TaskId,
                CommitId = snapshot.CommitId,
                ImageDigest = snapshot.Image.Digest,
                Document = snapshot.ToJson(),
                TakenAt = snapshot.TakenAt,
            });
        }

        context.StepRuns.Add(await NewEntryAsync(context, dispatch.FirstStep, cancellationToken));
        await AppendIntentsAsync(context, dispatch.Effects, at, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> PlanStepRunAsync(NewStepRun run, CancellationToken cancellationToken)
    {
        await using var context = database.OpenWriter();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var at = UtcInstantConverter.ToText(time.GetUtcNow());
        var taskId = EntityIdConverter.ToText(run.TaskId);
        var running = Core.Tasks.TaskStates.Tag(Core.Tasks.TaskState.Running);

        // The first statement is a write, for the reason given in ApplyAsync; it also holds the plan to a Task that is still running.
        var claimed = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Tasks" SET "UpdatedAt" = {at} WHERE "Id" = {taskId} AND "State" = {running}""",
            cancellationToken);
        if (claimed == 0 || await context.StepRuns.AnyAsync(s => s.Id == run.Id, cancellationToken))
        {
            return false;
        }

        context.StepRuns.Add(await NewEntryAsync(context, run, cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ChangeStepRunAsync(StepRunChange change, CancellationToken cancellationToken)
    {
        await using var context = database.OpenWriter();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var at = UtcInstantConverter.ToText(time.GetUtcNow());
        if (!await WriteStepRunChangeAsync(context, change, at, cancellationToken))
        {
            return false;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> CloseWindowAsync(WindowClosing closing, CancellationToken cancellationToken)
    {
        await using var context = database.OpenWriter();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (!await CloseWindowAsync(context, closing, UtcInstantConverter.ToText(time.GetUtcNow()), cancellationToken))
        {
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task<bool> CloseWindowAsync(HephaestusDbContext context, WindowClosing closing, string at, CancellationToken cancellationToken)
    {
        var id = EntityIdConverter.ToText(closing.WindowId);
        var end = closing.End.Tag();
        return await context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Windows" SET "ClosedAt" = {at}, "End" = {end} WHERE "Id" = {id} AND "ClosedAt" IS NULL""",
            cancellationToken) > 0;
    }

    /// <summary>The sequence of a step run is its place among its Task's: one more than the last, made here, where the one writer cannot be raced.</summary>
    private static async Task<StepRunEntry> NewEntryAsync(HephaestusDbContext context, NewStepRun run, CancellationToken cancellationToken) => new()
    {
        Id = run.Id,
        TaskId = run.TaskId,
        Sequence = (await context.StepRuns.Where(s => s.TaskId == run.TaskId).MaxAsync(s => (int?)s.Sequence, cancellationToken) ?? 0) + 1,
        Kind = run.Kind.Tag(),
        Round = run.Round,
        Status = StepRunStatus.Planned.Tag(),
        Deadline = run.Deadline,
        PlannedAt = run.PlannedAt,
        Agent = run.Agent?.Tag(),
        Model = run.Model,
        Effort = run.Effort?.Tag(),
        Methodology = run.Methodology,
        Template = run.Template,
        CommandIndex = run.CommandIndex,
        TipBefore = run.TipBefore,
    };

    /// <summary>
    /// Changes the step run while it is in a status the change may be made from, and, with it, moves the Task and journals the intents the change causes. The first statement is the write that makes the change, so that a change
    /// that finds the run moved already writes nothing at all. Returns whether it was made.
    /// </summary>
    private static async Task<bool> WriteStepRunChangeAsync(HephaestusDbContext context, StepRunChange change, string at, CancellationToken cancellationToken)
    {
        var id = EntityIdConverter.ToText(change.StepRunId);
        var status = change.To.Tag();
        var from = change.To.MayMoveToFrom();
        if (from.Count == 0)
        {
            throw new ArgumentException("A step run does not move to the status it is planned in.", nameof(change));
        }

        var first = from[0].Tag();
        var second = from[^1].Tag();
        var failure = change.Failure?.Tag() ?? change.End?.Failure?.Tag();
        var changed = change.To switch
        {
            StepRunStatus.Started => await context.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "StepRuns" SET "Status" = {status}, "StartedAt" = {at} WHERE "Id" = {id} AND ("Status" = {first} OR "Status" = {second})""",
                cancellationToken),
            StepRunStatus.Ended => await EndAsync(context, change, id, status, first, second, failure, cancellationToken),
            StepRunStatus.Checkpointed => await context.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "StepRuns" SET "Status" = {status}, "TipAfter" = {change.TipAfter} WHERE "Id" = {id} AND ("Status" = {first} OR "Status" = {second})""",
                cancellationToken),
            _ => await context.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "StepRuns" SET "Status" = {status}, "Failure" = COALESCE({failure}, "Failure") WHERE "Id" = {id} AND ("Status" = {first} OR "Status" = {second})""",
                cancellationToken),
        };
        if (changed == 0)
        {
            return false;
        }

        var taskId = await context.StepRuns.AsNoTracking().Where(s => s.Id == change.StepRunId).Select(s => s.TaskId).SingleAsync(cancellationToken);
        if (change.Move is { } move)
        {
            var to = Core.Tasks.TaskStates.Tag(move.To);
            var leaving = Core.Tasks.TaskStates.Tag(move.From);
            var taskText = EntityIdConverter.ToText(taskId);
            var moved = await context.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Tasks" SET "State" = {to}, "UpdatedAt" = {at} WHERE "Id" = {taskText} AND "State" = {leaving}""",
                cancellationToken);
            if (moved > 0)
            {
                await AppendIntentsAsync(context, move.Effects, at, cancellationToken);
            }
        }

        if (change.Effects is { Count: > 0 } effects)
        {
            await AppendIntentsAsync(context, effects, at, cancellationToken);
        }

        return true;
    }

    private static Task<int> EndAsync(HephaestusDbContext context, StepRunChange change, string id, string status, string first, string second, string? failure, CancellationToken cancellationToken)
    {
        var end = change.End ?? throw new ArgumentException("The end of a step run says how it ended.", nameof(change));
        var endedAt = UtcInstantConverter.ToText(end.At);
        var cause = end.Cause.Tag();
        return context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "StepRuns" SET "Status" = {status}, "EndedAt" = {endedAt}, "ExitCode" = {end.ExitCode}, "Cause" = {cause}, "Failure" = {failure}, "Outcome" = {end.Outcome}, "Result" = {end.Result}
            WHERE "Id" = {id} AND ("Status" = {first} OR "Status" = {second})
            """,
            cancellationToken);
    }

    public async Task<bool> EscalateAsync(EscalationPlan plan, CancellationToken cancellationToken)
    {
        await using var context = database.OpenWriter();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var at = UtcInstantConverter.ToText(time.GetUtcNow());
        if (!await WriteEscalationAsync(context, plan, at, cancellationToken))
        {
            return false;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Records the Escalation, moves the Task, and journals the intents that show it, as one change. The move is made only while the Task is in the
    /// state the plan was made for, so that one Escalation asks for one decision; otherwise nothing is written and false is returned.
    /// </summary>
    private static async Task<bool> WriteEscalationAsync(HephaestusDbContext context, EscalationPlan plan, string at, CancellationToken cancellationToken)
    {
        var escalation = plan.Escalation;
        var taskId = EntityIdConverter.ToText(escalation.TaskId);
        var to = Core.Tasks.TaskStates.Tag(plan.To);
        var from = Core.Tasks.TaskStates.Tag(plan.From);
        var moved = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Tasks" SET "State" = {to}, "UpdatedAt" = {at} WHERE "Id" = {taskId} AND "State" = {from}""",
            cancellationToken);
        if (moved == 0)
        {
            return false;
        }

        context.Escalations.Add(new EscalationEntry
        {
            Id = escalation.Id,
            TaskId = escalation.TaskId,
            Kind = escalation.Kind.Tag(),
            Decision = escalation.Decision,
            Options = JsonSerializer.Serialize(escalation.Options),
            ResumeCondition = escalation.ResumeCondition,
            Cause = escalation.Cause,
            RaisedAt = escalation.RaisedAt,
            EffectKey = escalation.Effect?.Key,
            EffectKind = escalation.Effect?.Kind.Tag(),
            EffectDetail = escalation.Effect?.Detail,
            EffectReason = escalation.Effect?.Reason,
            Check = escalation.Check?.Tag(),
        });
        await AppendIntentsAsync(context, plan.Effects, at, cancellationToken);
        return true;
    }

    public async Task<bool> RecordEffectAsync(EffectRecording recording, CancellationToken cancellationToken)
    {
        await using var context = database.OpenWriter();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // The first statement is a write, for the reason given in ApplyAsync. It appends the line only while the effect has no result, so
        // that a second recording of the end of an effect is left out, whatever else is going on.
        var effect = recording.Effect;
        var at = UtcInstantConverter.ToText(time.GetUtcNow());
        var entry = (recording.Result is null ? JournalEntryKind.Retry : JournalEntryKind.Result).Tag();
        var result = recording.Result?.Tag();
        var next = recording.NextAttemptAt is { } instant ? UtcInstantConverter.ToText(instant) : null;
        var taskId = effect.TaskId is { } id ? EntityIdConverter.ToText(id) : null;
        var kind = effect.Kind.Tag();
        var resultEntry = JournalEntryKind.Result.Tag();
        var appended = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "EffectJournal" ("Key", "Entry", "TaskId", "RepositoryId", "IssueNumber", "Cause", "Kind", "Detail", "Ordinal", "Result", "Reason", "Failures", "NextAttemptAt", "At")
            SELECT {effect.Key}, {entry}, {taskId}, {effect.RepositoryId}, {effect.IssueNumber}, {effect.Cause}, {kind}, {effect.Detail}, {effect.Ordinal}, {result}, {recording.Reason}, {recording.Failures}, {next}, {at}
            WHERE NOT EXISTS (SELECT 1 FROM "EffectJournal" WHERE "Key" = {effect.Key} AND "Entry" = {resultEntry})
            """,
            cancellationToken);
        if (appended == 0)
        {
            return false;
        }

        // The Escalation is written with the result, in this transaction, or not at all: when the Task is no longer where the plan found it,
        // the result still stands and the Task is left as it is.
        if (recording.Escalation is { } plan)
        {
            await WriteEscalationAsync(context, plan, at, cancellationToken);
        }

        // The move the effect causes is made with its result, while the Task is where the effect found it, and the intents that show it are journaled with the move.
        if (recording.Move is { } move && effect.TaskId is not null)
        {
            var to = Core.Tasks.TaskStates.Tag(move.To);
            var from = Core.Tasks.TaskStates.Tag(move.From);
            var moved = await context.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Tasks" SET "State" = {to}, "UpdatedAt" = {at} WHERE "Id" = {taskId} AND "State" = {from}""",
                cancellationToken);
            if (moved > 0)
            {
                await AppendIntentsAsync(context, move.Effects, at, cancellationToken);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Adds the intents of effects to the journal. The key is made here, from the ordinal: how many effects of the kind the cause had in the
    /// scope before, counting those of this transaction, which the database has not seen yet. An effect that names a notice is left out when
    /// the journal has an intent for the same notice on the same issue already, or this transaction has just added one.
    /// </summary>
    private static async Task AppendIntentsAsync(HephaestusDbContext context, IReadOnlyList<NewEffect> effects, string at, CancellationToken cancellationToken)
    {
        var added = new Dictionary<(string Scope, string Cause, EffectKind Kind), int>();
        var notices = new HashSet<(long? RepositoryId, int? IssueNumber, string NoticeKey)>();
        var intent = JournalEntryKind.Intent.Tag();
        foreach (var effect in effects)
        {
            if (effect.NoticeKey is { } noticeKey)
            {
                var said = !notices.Add((effect.RepositoryId, effect.IssueNumber, noticeKey))
                    || await context.EffectJournal.AnyAsync(e => e.Entry == intent && e.RepositoryId == effect.RepositoryId && e.IssueNumber == effect.IssueNumber && e.NoticeKey == noticeKey, cancellationToken);
                if (said)
                {
                    continue;
                }
            }

            var scope = effect.Scope;
            var group = (scope, effect.Cause, effect.Kind);
            var kind = effect.Kind.Tag();
            var stored = await context.EffectJournal.CountAsync(
                e => e.Entry == intent
                    && e.Cause == effect.Cause
                    && e.Kind == kind
                    && (effect.TaskId != null ? e.TaskId == effect.TaskId : e.TaskId == null && e.RepositoryId == effect.RepositoryId),
                cancellationToken);
            var ordinal = stored + added.GetValueOrDefault(group);
            added[group] = added.GetValueOrDefault(group) + 1;
            context.EffectJournal.Add(new EffectJournalEntryEntity
            {
                Key = EffectKeys.For(scope, effect.Cause, effect.Kind, ordinal),
                Entry = intent,
                TaskId = effect.TaskId,
                RepositoryId = effect.RepositoryId,
                IssueNumber = effect.IssueNumber,
                NoticeKey = effect.NoticeKey,
                Cause = effect.Cause,
                Kind = kind,
                Detail = effect.Detail,
                Payload = effect.Payload,
                Ordinal = ordinal,
                At = UtcInstantConverter.FromText(at),
            });
        }
    }
}
