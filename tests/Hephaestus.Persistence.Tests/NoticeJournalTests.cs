using Hephaestus.Core.Effects;
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

/// <summary>
/// Story 2.5, AD-5, NFR-7, NFR-10: a comment on an issue that is no Task is journaled under the Project, one for each user, kind of action, and
/// issue, and the journal that Story 2.4 made is carried over with its rules.
/// </summary>
public class NoticeJournalTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<TempInstallation> StartAsync()
    {
        var installation = new TempInstallation(new FixedTimeProvider(Noon));
        await installation.MigrateAsync();
        return installation;
    }

    private static Signal ByBob(string id, int issue = 7, long actor = 1001, string login = "bob") =>
        new(Signal.GitHubSource, id, SignalKind.Labeled, 101, issue, actor, login, "hephaestus", null, null, Noon, "Add a login page", "Details.");

    private static NewEffect Notice(string cause, int issue = 7, long actor = 1001, string login = "bob", Guid? task = null) =>
        IssueNotices.IgnoredLabel(task, cause, 101, issue, actor, login, "hephaestus")!;

    private static async Task AppendAsync(TempInstallation installation, params Signal[] signals) =>
        await installation.Provider.GetRequiredService<ISignalInbox>().AppendAsync(signals, Token);

    private static Task<bool> RejectAsync(TempInstallation installation, long sequence, params NewEffect[] effects) =>
        installation.Provider.GetRequiredService<IUnitOfWork>().ApplyAsync(
            new SignalApplication(sequence, SignalStatus.Rejected, "actor-not-trusted", NewEffects: effects),
            Token);

    [Fact]
    public async Task A_comment_for_an_issue_that_is_no_Task_is_journaled_with_the_Signal_under_a_key_of_the_Project_and_names_the_issue()
    {
        using var installation = await StartAsync();
        await AppendAsync(installation, ByBob("issue-event:1"));

        Assert.True(await RejectAsync(installation, 1, Notice("g1")));

        var entry = Assert.Single(await installation.Reads.ListJournalAsync(null, Token));
        Assert.Equal(JournalEntryKind.Intent, entry.Entry);
        Assert.Equal(new Effect("project:101:g1:comment:0", null, "g1", EffectKind.Comment, "ignored-label|bob|hephaestus", 0, 101, 7), entry.Effect);
        var pending = Assert.Single(await installation.Reads.ListUnfinishedEffectsAsync(Token));
        Assert.Equal((entry.Effect, null, 0), (pending.Effect, pending.Task, pending.Failures));
        Assert.Equal(SignalStatus.Rejected, Assert.Single(await installation.Provider.GetRequiredService<ISignalInbox>().ReadAsync(0, 10, Token)).Status);
    }

    [Fact]
    public async Task The_result_of_such_a_comment_is_recorded_with_its_scope_and_ends_it_and_a_retry_shows_how_its_attempts_went()
    {
        using var installation = await StartAsync();
        await AppendAsync(installation, ByBob("issue-event:1"));
        await RejectAsync(installation, 1, Notice("g1"));
        var unit = installation.Provider.GetRequiredService<IUnitOfWork>();
        var effect = Assert.Single(await installation.Reads.ListUnfinishedEffectsAsync(Token)).Effect;

        Assert.True(await unit.RecordEffectAsync(new EffectRecording(effect, null, "unreachable", 1, Noon.AddSeconds(30)), Token));
        var retried = Assert.Single(await installation.Reads.ListUnfinishedEffectsAsync(Token));
        Assert.Equal((1, (DateTimeOffset?)Noon.AddSeconds(30)), (retried.Failures, retried.NextAttemptAt));
        Assert.True(await unit.RecordEffectAsync(new EffectRecording(effect, EffectResult.GaveUp, "unreachable", 2, null), Token));
        Assert.False(await unit.RecordEffectAsync(new EffectRecording(effect, EffectResult.Applied, null, 2, null), Token));

        Assert.Empty(await installation.Reads.ListUnfinishedEffectsAsync(Token));
        var journal = await installation.Reads.ListJournalAsync(null, Token);
        Assert.Equal([JournalEntryKind.Intent, JournalEntryKind.Retry, JournalEntryKind.Result], journal.Select(e => e.Entry));
        Assert.All(journal, line => Assert.Equal((effect.Key, null, 101L, 7), (line.Effect.Key, line.Effect.TaskId, line.Effect.RepositoryId, line.Effect.IssueNumber)));
    }

    [Fact]
    public async Task The_same_user_and_kind_of_action_on_the_same_issue_is_journaled_once_however_often_it_is_rejected()
    {
        using var installation = await StartAsync();
        await AppendAsync(installation, ByBob("issue-event:1"), ByBob("issue-event:2"), ByBob("issue-event:3"));

        Assert.True(await RejectAsync(installation, 1, Notice("g1")));
        Assert.True(await RejectAsync(installation, 2, Notice("g2")));
        Assert.True(await RejectAsync(installation, 3, Notice("g3"), Notice("g3")));

        Assert.Equal(["project:101:g1:comment:0"], (await installation.Reads.ListJournalAsync(null, Token)).Select(e => e.Effect.Key));
        Assert.All(await installation.Provider.GetRequiredService<ISignalInbox>().ReadAsync(0, 10, Token), signal => Assert.Equal(SignalStatus.Rejected, signal.Status));
    }

    [Fact]
    public async Task Another_user_another_issue_or_a_second_effect_of_the_same_cause_is_journaled_and_the_ordinal_counts_them_in_the_scope()
    {
        using var installation = await StartAsync();
        await AppendAsync(installation, ByBob("issue-event:1"), ByBob("issue-event:2", actor: 1002, login: "carol"), ByBob("issue-event:3", issue: 8));

        await RejectAsync(installation, 1, Notice("g1"), Notice("g1", actor: 1002, login: "carol"));
        await RejectAsync(installation, 2, Notice("g2", actor: 1002, login: "carol", issue: 8));
        await RejectAsync(installation, 3, Notice("g3", issue: 8));

        var keys = (await installation.Reads.ListJournalAsync(null, Token)).Select(e => e.Effect.Key);
        Assert.Equal(["project:101:g1:comment:0", "project:101:g1:comment:1", "project:101:g2:comment:0", "project:101:g3:comment:0"], keys);
    }

    [Fact]
    public async Task A_comment_on_a_Tasks_issue_is_journaled_for_the_Task_and_the_notice_is_still_the_issues()
    {
        using var installation = await StartAsync();
        var task = new TaskRecord(EntityId.New(TimeProvider.System), 101, 7, TaskState.Queued, "hephaestus/7-add-a-login-page", Noon, Noon);
        var unit = installation.Provider.GetRequiredService<IUnitOfWork>();
        await AppendAsync(installation, ByBob("issue-event:1", actor: 29, login: "alice"), ByBob("issue-event:2"));
        await unit.ApplyAsync(
            new SignalApplication(1, SignalStatus.Applied, TaskId: task.Id, NewTask: new NewTask(task, new IssueSnapshot("Add a login page", "Details.", IssueContent.Hash("Add a login page", "Details."), 1, Noon)), NewEffects: [new NewEffect(task.Id, "g1", EffectKind.Label, "queued")]),
            Token);

        await RejectAsync(installation, 2, Notice("g2", task: task.Id));

        var comment = (await installation.Reads.ListJournalAsync(task.Id, Token)).Single(e => e.Effect.Kind == EffectKind.Comment);
        Assert.Equal(($"{task.Id}:g2:comment:0", task.Id, 101L, 7), (comment.Effect.Key, comment.Effect.TaskId, comment.Effect.RepositoryId, comment.Effect.IssueNumber));
        var pending = (await installation.Reads.ListUnfinishedEffectsAsync(Token)).Single(p => p.Effect.Kind == EffectKind.Comment);
        Assert.Equal(task, pending.Task);
        Assert.Single(await installation.Reads.ListJournalAsync(null, Token), e => e.Effect.Kind == EffectKind.Comment);
    }

    [Fact]
    public async Task The_database_refuses_a_second_intent_for_one_notice_even_when_something_other_than_the_loop_tries()
    {
        using var installation = await StartAsync();
        await AppendAsync(installation, ByBob("issue-event:1"));
        await RejectAsync(installation, 1, Notice("g1"));
        await using var context = installation.Database.OpenWriter();

        var exception = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "EffectJournal" ("Key", "Entry", "RepositoryId", "IssueNumber", "NoticeKey", "Cause", "Kind", "Detail", "Ordinal", "Failures", "At")
            VALUES ('project:101:g9:comment:0', 'intent', 101, 7, 'ignored-label:1001', 'g9', 'comment', 'x', 0, 0, '2026-11-27T12:00:00.0000000Z')
            """,
            Token));

        Assert.Contains("UNIQUE", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_journal_stays_append_only_after_the_table_was_made_again()
    {
        using var installation = await StartAsync();
        await AppendAsync(installation, ByBob("issue-event:1"));
        await RejectAsync(installation, 1, Notice("g1"));
        await using var context = installation.Database.OpenWriter();

        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync("""UPDATE "EffectJournal" SET "Detail" = 'x'""", Token));
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync("""DELETE FROM "EffectJournal" """, Token));
    }

    [Fact]
    public async Task A_journal_that_Story_2_4_wrote_is_carried_over_as_it_was_and_still_has_one_intent_and_one_result_for_a_key()
    {
        using var installation = new TempInstallation(new FixedTimeProvider(Noon));
        Directory.CreateDirectory(installation.Options.DataDirectory);
        await using (var context = installation.Database.OpenWriter())
        {
            await context.GetService<IMigrator>().MigrateAsync("20261127300000_AddEffectJournal", Token);
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Tasks" ("Id", "RepositoryId", "IssueNumber", "State", "Branch", "CreatedAt", "UpdatedAt")
                VALUES ('0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b', 101, 7, 'queued', 'hephaestus/7', '2026-11-27T12:00:00.0000000Z', '2026-11-27T12:00:00.0000000Z');
                INSERT INTO "EffectJournal" ("Key", "Entry", "TaskId", "Cause", "Kind", "Detail", "Ordinal", "Failures", "At")
                VALUES ('0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b:g4:label:0', 'intent', '0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b', 'g4', 'label', 'queued', 0, 0, '2026-11-27T12:00:00.0000000Z');
                INSERT INTO "EffectJournal" ("Key", "Entry", "TaskId", "Cause", "Kind", "Detail", "Ordinal", "Result", "Failures", "At")
                VALUES ('0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b:g4:label:0', 'result', '0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b', 'g4', 'label', 'queued', 0, 'applied', 0, '2026-11-27T12:00:01.0000000Z');
                """,
                Token);
        }

        await installation.MigrateAsync();

        var journal = await installation.Reads.ListJournalAsync(null, Token);
        Assert.Equal([(1L, JournalEntryKind.Intent, null), (2L, JournalEntryKind.Result, EffectResult.Applied)], journal.Select(e => (e.Sequence, e.Entry, e.Result)));
        Assert.All(journal, line => Assert.Equal(("0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b:g4:label:0", (long?)null, (int?)null), (line.Effect.Key, line.Effect.RepositoryId, line.Effect.IssueNumber)));
        await using var context2 = installation.Database.OpenWriter();
        await Assert.ThrowsAsync<SqliteException>(() => context2.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "EffectJournal" ("Key", "Entry", "TaskId", "Cause", "Kind", "Detail", "Ordinal", "Failures", "At")
            VALUES ('0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b:g4:label:0', 'intent', '0197a1b2-c3d4-7e5f-8a6b-7c8d9e0f1a2b', 'g4', 'label', 'queued', 0, 0, '2026-11-27T12:00:02.0000000Z')
            """,
            Token));
        await AppendAsync(installation, ByBob("issue-event:1"));
        Assert.True(await RejectAsync(installation, 1, Notice("g5")));
        Assert.Equal(3, (await installation.Reads.ListJournalAsync(null, Token)).Count);
    }
}
