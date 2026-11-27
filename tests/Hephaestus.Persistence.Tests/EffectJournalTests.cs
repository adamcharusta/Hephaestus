using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.Identity;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Persistence.Tests;

/// <summary>Story 2.4, AD-5, NFR-7: the effect journal is append-only, an intent commits with the state change that causes it, and a result is recorded after.</summary>
public class EffectJournalTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<TempInstallation> StartAsync()
    {
        var installation = new TempInstallation(new FixedTimeProvider(Noon));
        await installation.MigrateAsync();
        return installation;
    }

    private static Signal Labeled(string id, int issue = 7, string label = "hephaestus") =>
        new(Signal.GitHubSource, id, SignalKind.Labeled, 101, issue, 29, "alice", label, null, null, Noon, "Add a login page", "Details.");

    private static TaskRecord NewTask(int issue = 7) =>
        new(EntityId.New(TimeProvider.System), 101, issue, TaskState.Queued, $"hephaestus/{issue}-add-a-login-page", Noon, Noon);

    private static IssueSnapshot Snapshot(long sequence = 1) =>
        new("Add a login page", "Details.", IssueContent.Hash("Add a login page", "Details."), sequence, Noon);

    private static async Task<TaskRecord> OptInAsync(TempInstallation installation, int issue = 7, long sequence = 1)
    {
        var task = NewTask(issue);
        await installation.Provider.GetRequiredService<ISignalInbox>().AppendAsync([Labeled($"issue-event:{sequence}", issue)], Token);
        await installation.Provider.GetRequiredService<IUnitOfWork>().ApplyAsync(
            new SignalApplication(sequence, SignalStatus.Applied, TaskId: task.Id, NewTask: new NewTask(task, Snapshot(sequence)), NewEffects: [new NewEffect(task.Id, EffectKeys.SignalCause(sequence), EffectKind.Label, "queued")]),
            Token);
        return task;
    }

    [Fact]
    public async Task The_intent_is_committed_with_the_Task_and_the_Signal_and_holds_the_key_the_cause_and_what_it_is_for()
    {
        using var installation = await StartAsync();

        var task = await OptInAsync(installation);

        var journal = await installation.Reads.ListJournalAsync(null, Token);
        var entry = Assert.Single(journal);
        Assert.Equal(JournalEntryKind.Intent, entry.Entry);
        Assert.Equal(new Effect($"{task.Id}:g1:label:0", task.Id, "g1", EffectKind.Label, "queued", 0), entry.Effect);
        Assert.Equal((null, null, 0, null, Noon), (entry.Result, entry.Reason, entry.Failures, entry.NextAttemptAt, entry.At));
        Assert.Equal(SignalStatus.Applied, Assert.Single(await installation.Provider.GetRequiredService<ISignalInbox>().ReadAsync(0, 10, Token)).Status);
    }

    [Fact]
    public async Task A_Signal_that_is_not_applied_leaves_no_intent_and_no_Task()
    {
        using var installation = await StartAsync();
        var task = NewTask();
        await installation.Provider.GetRequiredService<ISignalInbox>().AppendAsync([Labeled("issue-event:1")], Token);
        var unit = installation.Provider.GetRequiredService<IUnitOfWork>();
        await unit.ApplyAsync(new SignalApplication(1, SignalStatus.Rejected, "actor-not-trusted"), Token);

        var applied = await unit.ApplyAsync(
            new SignalApplication(1, SignalStatus.Applied, TaskId: task.Id, NewTask: new NewTask(task, Snapshot()), NewEffects: [new NewEffect(task.Id, "g1", EffectKind.Label, "queued")]),
            Token);

        Assert.False(applied);
        Assert.Empty(await installation.Reads.ListJournalAsync(null, Token));
        Assert.Null(await installation.Reads.FindTaskAsync(101, 7, Token));
    }

    [Fact]
    public async Task An_intent_whose_Task_cannot_be_written_is_not_journaled_either()
    {
        using var installation = await StartAsync();
        await OptInAsync(installation);
        var again = NewTask();
        await installation.Provider.GetRequiredService<ISignalInbox>().AppendAsync([Labeled("issue-event:2")], Token);

        await Assert.ThrowsAsync<DbUpdateException>(() => installation.Provider.GetRequiredService<IUnitOfWork>().ApplyAsync(
            new SignalApplication(2, SignalStatus.Applied, TaskId: again.Id, NewTask: new NewTask(again, Snapshot(2)), NewEffects: [new NewEffect(again.Id, "g2", EffectKind.Label, "queued")]),
            Token));

        Assert.Single(await installation.Reads.ListJournalAsync(null, Token));
    }

    [Fact]
    public async Task The_ordinal_counts_the_effects_of_a_kind_that_a_cause_already_had_for_the_Task_and_so_does_the_key()
    {
        using var installation = await StartAsync();
        var task = await OptInAsync(installation);
        var unit = installation.Provider.GetRequiredService<IUnitOfWork>();
        await installation.Provider.GetRequiredService<ISignalInbox>().AppendAsync([Labeled("issue-event:2", label: "hephaestus:queued")], Token);

        await unit.ApplyAsync(
            new SignalApplication(2, SignalStatus.Applied, TaskId: task.Id, NewEffects: [new NewEffect(task.Id, "g1", EffectKind.Label, "queued"), new NewEffect(task.Id, "g1", EffectKind.Label, "queued"), new NewEffect(task.Id, "g2", EffectKind.Label, "queued")]),
            Token);

        var keys = (await installation.Reads.ListJournalAsync(task.Id, Token)).Select(e => e.Effect.Key);
        Assert.Equal([$"{task.Id}:g1:label:0", $"{task.Id}:g1:label:1", $"{task.Id}:g1:label:2", $"{task.Id}:g2:label:0"], keys);
    }

    [Fact]
    public async Task An_effect_without_a_result_is_unfinished_with_its_Task_and_a_retry_shows_how_its_attempts_went()
    {
        using var installation = await StartAsync();
        var task = await OptInAsync(installation);
        var unit = installation.Provider.GetRequiredService<IUnitOfWork>();
        var pending = Assert.Single(await installation.Reads.ListUnfinishedEffectsAsync(Token));
        Assert.Equal((task, 0, null), (pending.Task, pending.Failures, pending.NextAttemptAt));

        var next = Noon.AddSeconds(30);
        Assert.True(await unit.RecordEffectAsync(new EffectRecording(pending.Effect, null, "unreachable", 1, next), Token));
        Assert.True(await unit.RecordEffectAsync(new EffectRecording(pending.Effect, null, "unreachable", 2, next.AddMinutes(1)), Token));

        var after = Assert.Single(await installation.Reads.ListUnfinishedEffectsAsync(Token));
        Assert.Equal((2, (DateTimeOffset?)next.AddMinutes(1)), (after.Failures, after.NextAttemptAt));
        Assert.Equal([JournalEntryKind.Intent, JournalEntryKind.Retry, JournalEntryKind.Retry], (await installation.Reads.ListJournalAsync(task.Id, Token)).Select(e => e.Entry));
    }

    [Fact]
    public async Task A_result_ends_the_effect_and_a_second_result_is_left_out()
    {
        using var installation = await StartAsync();
        var task = await OptInAsync(installation);
        var unit = installation.Provider.GetRequiredService<IUnitOfWork>();
        var effect = Assert.Single(await installation.Reads.ListUnfinishedEffectsAsync(Token)).Effect;

        Assert.True(await unit.RecordEffectAsync(new EffectRecording(effect, EffectResult.Applied, null, 0, null), Token));
        Assert.False(await unit.RecordEffectAsync(new EffectRecording(effect, EffectResult.Refused, "rejected", 0, null, EscalationPath.ForFailedEffect(task, effect, "rejected", new FixedTimeProvider(Noon))), Token));
        Assert.False(await unit.RecordEffectAsync(new EffectRecording(effect, null, "unreachable", 1, Noon), Token));

        Assert.Empty(await installation.Reads.ListUnfinishedEffectsAsync(Token));
        var journal = await installation.Reads.ListJournalAsync(null, Token);
        Assert.Equal([JournalEntryKind.Intent, JournalEntryKind.Result], journal.Select(e => e.Entry));
        Assert.Equal(EffectResult.Applied, journal[1].Result);
        Assert.Equal(TaskState.Queued, (await installation.Reads.FindTaskAsync(101, 7, Token))!.State);
    }

    [Fact]
    public async Task A_result_that_ends_an_effect_for_good_moves_the_Task_and_journals_the_intent_that_shows_it_in_the_same_transaction()
    {
        using var installation = await StartAsync();
        var task = await OptInAsync(installation);
        var effect = Assert.Single(await installation.Reads.ListUnfinishedEffectsAsync(Token)).Effect;

        await installation.Provider.GetRequiredService<IUnitOfWork>().RecordEffectAsync(
            new EffectRecording(effect, EffectResult.GaveUp, "unreachable", 6, null, EscalationPath.ForFailedEffect(task, effect, "unreachable", new FixedTimeProvider(Noon))),
            Token);

        var moved = (await installation.Reads.FindTaskAsync(101, 7, Token))!;
        Assert.Equal((TaskState.WaitingForOwner, Noon), (moved.State, moved.UpdatedAt));
        var journal = await installation.Reads.ListJournalAsync(task.Id, Token);
        
        // The label that shows the move, and the comment and the notification of the Escalation the move raised.
        Assert.Equal([JournalEntryKind.Intent, JournalEntryKind.Result, JournalEntryKind.Intent, JournalEntryKind.Intent, JournalEntryKind.Intent], journal.Select(e => e.Entry));
        Assert.Equal((EffectResult.GaveUp, "unreachable", 6), (journal[1].Result, journal[1].Reason, journal[1].Failures));
        Assert.Equal(($"{task.Id}:g1:label:1", "waiting-for-owner"), (journal[2].Effect.Key, journal[2].Effect.Detail));
        var unfinished = await installation.Reads.ListUnfinishedEffectsAsync(Token);
        Assert.Equal([EffectKind.Label, EffectKind.Comment, EffectKind.Notification], unfinished.Select(e => e.Effect.Kind));
        Assert.All(unfinished, pending => Assert.Equal(TaskState.WaitingForOwner, pending.Task!.State));
    }

    [Fact]
    public async Task The_journal_is_append_only_a_line_can_neither_be_changed_nor_removed_by_anything()
    {
        using var installation = await StartAsync();
        await OptInAsync(installation);
        await using var context = installation.Database.OpenWriter();

        var update = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync("UPDATE EffectJournal SET Detail = 'running'", Token));
        var delete = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync("DELETE FROM EffectJournal", Token));

        Assert.Contains("append-only", update.Message, StringComparison.Ordinal);
        Assert.Contains("append-only", delete.Message, StringComparison.Ordinal);
        Assert.Single(await installation.Reads.ListJournalAsync(null, Token));
    }

    [Fact]
    public async Task The_database_refuses_a_second_intent_or_a_second_result_for_one_key_and_allows_any_number_of_retries()
    {
        using var installation = await StartAsync();
        var task = await OptInAsync(installation);
        await using var context = installation.Database.OpenWriter();
        var key = $"{task.Id}:g1:label:0";

        Task Insert(string entry) => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "EffectJournal" ("Key", "Entry", "TaskId", "Cause", "Kind", "Detail", "Ordinal", "Failures", "At")
            VALUES ({key}, {entry}, {task.Id.ToString("D")}, 'g1', 'label', 'queued', 0, 0, '2026-11-27T12:00:00.0000000Z')
            """,
            Token);

        await Assert.ThrowsAsync<SqliteException>(() => Insert("intent"));
        await Insert("retry");
        await Insert("retry");
        await Insert("result");
        await Assert.ThrowsAsync<SqliteException>(() => Insert("result"));
    }

    [Fact]
    public async Task The_journal_is_read_for_one_Task_or_for_all_in_the_order_it_was_written()
    {
        using var installation = await StartAsync();
        var first = await OptInAsync(installation, issue: 7, sequence: 1);
        var second = await OptInAsync(installation, issue: 8, sequence: 2);

        Assert.Equal([first.Id, second.Id], (await installation.Reads.ListJournalAsync(null, Token)).Select(e => e.Effect.TaskId));
        Assert.Equal([second.Id], (await installation.Reads.ListJournalAsync(second.Id, Token)).Select(e => e.Effect.TaskId));
        Assert.Empty(await installation.Reads.ListJournalAsync(EntityId.New(TimeProvider.System), Token));
        Assert.Equal([first.Id, second.Id], (await installation.Reads.ListUnfinishedEffectsAsync(Token)).Select(p => p.Task!.Id));
    }

    [Fact]
    public async Task A_restart_finds_the_unfinished_effect_where_it_was_left()
    {
        using var installation = await StartAsync();
        var task = await OptInAsync(installation);
        await installation.Provider.GetRequiredService<IUnitOfWork>().RecordEffectAsync(
            new EffectRecording(Assert.Single(await installation.Reads.ListUnfinishedEffectsAsync(Token)).Effect, null, "unreachable", 1, Noon.AddMinutes(1)),
            Token);

        await using var restarted = installation.Restart();

        var pending = Assert.Single(await restarted.GetRequiredService<IReadStore>().ListUnfinishedEffectsAsync(Token));
        Assert.Equal((task.Id, 1, (DateTimeOffset?)Noon.AddMinutes(1)), (pending.Task!.Id, pending.Failures, pending.NextAttemptAt));
    }

    [Fact]
    public async Task A_journal_line_with_a_kind_or_an_entry_nobody_knows_is_a_fault_and_not_a_guess()
    {
        using var installation = await StartAsync();
        var task = await OptInAsync(installation);
        await using (var context = installation.Database.OpenWriter())
        {
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER EffectJournal_NoUpdate", Token);
            await context.Database.ExecuteSqlRawAsync("UPDATE EffectJournal SET Kind = 'push'", Token);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => installation.Reads.ListJournalAsync(task.Id, Token));

        await using (var context = installation.Database.OpenWriter())
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE EffectJournal SET Kind = 'label', Entry = 'intention'", Token);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => installation.Reads.ListJournalAsync(task.Id, Token));
    }

    [Fact]
    public async Task A_Task_that_existed_before_the_journal_gets_the_intent_to_show_its_state_from_the_migration_and_the_Signal_that_made_it_is_the_cause()
    {
        using var installation = new TempInstallation(new FixedTimeProvider(Noon));
        Directory.CreateDirectory(installation.Options.DataDirectory);
        await using (var context = installation.Database.OpenWriter())
        {
            await context.GetService<IMigrator>().MigrateAsync("20261127200000_AddTasks", Token);
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Tasks" ("Id", "RepositoryId", "IssueNumber", "State", "Branch", "CreatedAt", "UpdatedAt")
                VALUES ('0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b', 101, 7, 'queued', 'hephaestus/7', '2026-11-27T12:00:00.0000000Z', '2026-11-27T12:00:00.0000000Z'),
                       ('0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2c', 101, 8, 'waiting-for-owner', 'hephaestus/8', '2026-11-27T12:00:00.0000000Z', '2026-11-27T12:00:00.0000000Z');
                INSERT INTO "IssueSnapshots" ("TaskId", "Title", "Body", "Hash", "SignalSequence", "TakenAt")
                VALUES ('0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b', 'a', '', 'h', 4, '2026-11-27T12:00:00.0000000Z'),
                       ('0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b', 'b', '', 'h', 9, '2026-11-27T12:05:00.0000000Z'),
                       ('0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2c', 'c', '', 'h', 6, '2026-11-27T12:00:00.0000000Z');
                """,
                Token);
        }

        await installation.MigrateAsync();

        var pending = await installation.Reads.ListUnfinishedEffectsAsync(Token);
        Assert.Equal(
            [("0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b:g4:label:0", "g4", "queued"), ("0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2c:g6:label:0", "g6", "waiting-for-owner")],
            pending.Select(p => (p.Effect.Key, p.Effect.Cause, p.Effect.Detail)));
    }
}
