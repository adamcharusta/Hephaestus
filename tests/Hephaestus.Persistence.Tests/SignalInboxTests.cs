using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Persistence.Tests;

/// <summary>Story 2.1, AD-1: Signals are appended to a persisted inbox in a database-assigned order, a second append of the same event is a no-op, and a cursor is one row per Project and stream.</summary>
public class SignalInboxTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<TempInstallation> StartAsync(TimeProvider? time = null)
    {
        var installation = new TempInstallation(time);
        await installation.MigrateAsync();
        return installation;
    }

    private static Signal Labeled(string id, int issue = 7, long? actor = 29) =>
        new(Signal.GitHubSource, id, SignalKind.Labeled, 101, issue, actor, actor is null ? null : "alice", "hephaestus", null, null, Noon);

    [Fact]
    public async Task A_new_installation_has_an_empty_inbox()
    {
        using var installation = await StartAsync();

        Assert.Empty(await installation.Provider.GetRequiredService<ISignalInbox>().ReadAsync(0, 100, Token));
    }

    [Fact]
    public async Task Signals_are_read_back_whole_in_the_order_they_were_appended_with_a_sequence_that_only_grows()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();
        var comment = new Signal(Signal.GitHubSource, "comment:9:edited:2026-11-27T12:00:00Z", SignalKind.CommentEdited, 101, 8, null, null, null, 9, 30, Noon.AddMinutes(1));

        var appended = await inbox.AppendAsync([Labeled("issue-event:2"), comment, Labeled("issue-event:1", actor: null)], Token);
        var stored = await inbox.ReadAsync(0, 100, Token);

        Assert.Equal(3, appended);
        Assert.Equal([1L, 2L, 3L], stored.Select(s => s.Sequence));
        Assert.Equal(["issue-event:2", "comment:9:edited:2026-11-27T12:00:00Z", "issue-event:1"], stored.Select(s => s.Signal.SourceEventId));
        Assert.Equal(Labeled("issue-event:2"), stored[0].Signal);
        Assert.Equal(comment, stored[1].Signal);
        Assert.Null(stored[2].Signal.ActorId);
        Assert.Null(stored[2].Signal.ActorLogin);
    }

    [Fact]
    public async Task A_step_exited_Signal_is_read_back_with_its_step_run_exit_code_and_cause_and_a_second_one_for_the_step_run_is_a_no_op()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();
        var stepRun = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");
        var exited = Signal.StepExited(stepRun, 137, Hephaestus.Core.Workspaces.StepExitCause.Timeout, Noon);

        Assert.Equal(1, await inbox.AppendAsync([exited], Token));
        Assert.Equal(0, await inbox.AppendAsync([Signal.StepExited(stepRun, 0, Hephaestus.Core.Workspaces.StepExitCause.Exited, Noon.AddMinutes(1))], Token));
        var stored = Assert.Single(await inbox.ReadPendingAsync(10, Token));

        Assert.Equal(exited, stored.Signal);
        Assert.Equal((Signal.RuntimeSource, stepRun.ToString("D"), SignalKind.StepExited), (stored.Signal.Source, stored.Signal.SourceEventId, stored.Signal.Kind));
        Assert.Equal((stepRun, 137, Hephaestus.Core.Workspaces.StepExitCause.Timeout), (stored.Signal.StepRunId, stored.Signal.ExitCode, stored.Signal.Cause));
    }

    [Fact]
    public async Task A_step_exited_Signal_keeps_the_hosts_the_egress_proxy_refused_in_order_and_none_as_null()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();
        var refused = Guid.Parse("018f4b6e-0000-7000-8000-000000000003");
        var clean = Guid.Parse("018f4b6e-0000-7000-8000-000000000004");

        await inbox.AppendAsync([Signal.StepExited(refused, 1, Hephaestus.Core.Workspaces.StepExitCause.Exited, Noon, ["evil.example", "169.254.169.254", "[::1]"]), Signal.StepExited(clean, 0, Hephaestus.Core.Workspaces.StepExitCause.Exited, Noon, [])], Token);

        var stored = await inbox.ReadAsync(0, 10, Token);
        Assert.Equal(["evil.example", "169.254.169.254", "[::1]"], stored[0].Signal.RefusedHosts!);
        Assert.Null(stored[1].Signal.RefusedHosts);
    }

    [Fact]
    public async Task A_step_exited_Signal_of_a_container_that_was_lost_holds_no_exit_code()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();
        var lost = Signal.StepExited(Guid.Parse("018f4b6e-0000-7000-8000-000000000002"), null, Hephaestus.Core.Workspaces.StepExitCause.Lost, Noon);

        await inbox.AppendAsync([lost], Token);

        Assert.Equal(lost, Assert.Single(await inbox.ReadAsync(0, 10, Token)).Signal);
    }

    [Fact]
    public async Task A_stored_cause_that_is_not_one_the_runtime_knows_is_refused_when_it_is_read()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();
        await inbox.AppendAsync([Signal.StepExited(Guid.Parse("018f4b6e-0000-7000-8000-000000000002"), 1, Hephaestus.Core.Workspaces.StepExitCause.Exited, Noon)], Token);
        await using (var context = installation.Database.OpenWriter())
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE \"Signals\" SET \"Cause\" = 'vanished'", Token);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => inbox.ReadAsync(0, 10, Token));
    }

    [Fact]
    public async Task A_second_append_of_the_same_source_and_event_id_is_a_no_op_and_no_error()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();
        await inbox.AppendAsync([Labeled("issue-event:1")], Token);

        var again = await inbox.AppendAsync([Labeled("issue-event:1"), Labeled("issue-event:1", issue: 99, actor: 5), Labeled("issue-event:2")], Token);

        Assert.Equal(1, again);
        var stored = await inbox.ReadAsync(0, 100, Token);
        Assert.Equal(["issue-event:1", "issue-event:2"], stored.Select(s => s.Signal.SourceEventId));
        Assert.Equal(7, stored[0].Signal.IssueNumber);
    }

    [Fact]
    public async Task The_same_event_id_from_another_source_is_another_Signal()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();

        var appended = await inbox.AppendAsync([Labeled("1"), Labeled("1") with { Source = "control-api" }], Token);

        Assert.Equal(2, appended);
    }

    [Fact]
    public async Task A_sequence_is_never_used_twice_so_the_order_holds_after_a_restart()
    {
        using var installation = await StartAsync();
        await installation.Provider.GetRequiredService<ISignalInbox>().AppendAsync([Labeled("issue-event:1"), Labeled("issue-event:2")], Token);

        using var restarted = installation.Restart();
        var inbox = restarted.GetRequiredService<ISignalInbox>();
        await inbox.AppendAsync([Labeled("issue-event:3")], Token);

        Assert.Equal([1L, 2L, 3L], (await inbox.ReadAsync(0, 100, Token)).Select(s => s.Sequence));
    }

    [Fact]
    public async Task Reading_goes_on_after_a_sequence_and_stops_at_the_limit()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();
        await inbox.AppendAsync([.. Enumerable.Range(1, 5).Select(i => Labeled($"issue-event:{i}"))], Token);

        var page = await inbox.ReadAsync(afterSequence: 2, limit: 2, Token);

        Assert.Equal([3L, 4L], page.Select(s => s.Sequence));
    }

    [Fact]
    public async Task Appending_nothing_changes_nothing()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();

        Assert.Equal(0, await inbox.AppendAsync([], Token));
        Assert.Empty(await inbox.ReadAsync(0, 10, Token));
    }

    [Fact]
    public async Task A_batch_is_one_transaction_so_a_fault_in_the_middle_stores_none_of_it()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();
        var tooLong = Labeled("issue-event:2") with { Source = null! };

        await Assert.ThrowsAnyAsync<Exception>(() => inbox.AppendAsync([Labeled("issue-event:1"), tooLong], Token));

        Assert.Empty(await inbox.ReadAsync(0, 10, Token));
    }

    [Fact]
    public async Task A_stored_kind_nobody_knows_is_reported_when_it_is_read()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();
        await using (var writer = installation.Database.OpenWriter())
        {
            await writer.Database.ExecuteSqlRawAsync("""INSERT INTO "Signals" ("Source","SourceEventId","Kind","RepositoryId","IssueNumber","OccurredAt") VALUES ('github','x','exploded',1,1,'2026-11-27T12:00:00.0000000Z')""", Token);
        }

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => inbox.ReadAsync(0, 10, Token));

        Assert.Contains("exploded", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_inbox_outlives_a_restart()
    {
        using var installation = await StartAsync();
        await installation.Provider.GetRequiredService<ISignalInbox>().AppendAsync([Labeled("issue-event:1")], Token);

        using var restarted = installation.Restart();

        Assert.Single(await restarted.GetRequiredService<ISignalInbox>().ReadAsync(0, 10, Token));
    }

    [Fact]
    public async Task A_cursor_is_stored_per_Project_and_stream_and_replaced_by_the_next_one()
    {
        using var installation = await StartAsync();
        var cursors = installation.Provider.GetRequiredService<IPollCursorStore>();
        Assert.Null(await cursors.GetAsync(101, PollStreams.IssueEvents, Token));

        await cursors.SetAsync(101, PollStreams.IssueEvents, new PollCursor("41", "W/\"abc\""), Token);
        await cursors.SetAsync(101, PollStreams.Comments, new PollCursor("2026-11-27T12:00:00Z", null), Token);
        await cursors.SetAsync(202, PollStreams.IssueEvents, new PollCursor("7", null), Token);
        await cursors.SetAsync(101, PollStreams.IssueEvents, new PollCursor("42", "W/\"def\""), Token);

        Assert.Equal(new PollCursor("42", "W/\"def\""), await cursors.GetAsync(101, PollStreams.IssueEvents, Token));
        Assert.Equal(new PollCursor("2026-11-27T12:00:00Z", null), await cursors.GetAsync(101, PollStreams.Comments, Token));
        Assert.Equal(new PollCursor("7", null), await cursors.GetAsync(202, PollStreams.IssueEvents, Token));
    }

    [Fact]
    public async Task A_cursor_outlives_a_restart()
    {
        using var installation = await StartAsync();
        await installation.Provider.GetRequiredService<IPollCursorStore>().SetAsync(101, PollStreams.IssueEvents, new PollCursor("41", "W/\"abc\""), Token);

        using var restarted = installation.Restart();

        Assert.Equal(new PollCursor("41", "W/\"abc\""), await restarted.GetRequiredService<IPollCursorStore>().GetAsync(101, PollStreams.IssueEvents, Token));
    }

    [Fact]
    public async Task Registering_a_Project_starts_the_watch_at_that_moment()
    {
        using var installation = await StartAsync(new FixedTimeProvider(Noon));

        await installation.Configuration.RegisterProjectAsync(101, "acme/shop", "main", Token);

        var since = await installation.Provider.GetRequiredService<IPollCursorStore>().GetAsync(101, PollStreams.Since, Token);
        Assert.Equal(Noon, DateTimeOffset.Parse(since!.Position, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal));
        Assert.Null(since.ETag);
    }

    [Fact]
    public async Task Registering_the_same_Project_again_or_changing_its_Base_Branch_keeps_the_cursors()
    {
        var clock = new MovingTime(Noon);
        using var installation = await StartAsync(clock);
        var cursors = installation.Provider.GetRequiredService<IPollCursorStore>();
        await installation.Configuration.RegisterProjectAsync(101, "acme/shop", "main", Token);
        await cursors.SetAsync(101, PollStreams.IssueEvents, new PollCursor("41", null), Token);
        clock.Now = Noon.AddDays(1);

        await installation.Configuration.RegisterProjectAsync(101, "acme/shop", "main", Token);
        await installation.Configuration.RegisterProjectAsync(101, "acme/shop", "develop", Token);

        Assert.Equal(new PollCursor("41", null), await cursors.GetAsync(101, PollStreams.IssueEvents, Token));
        var since = await cursors.GetAsync(101, PollStreams.Since, Token);
        Assert.StartsWith("2026-11-27T12:00:00", since!.Position, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Removing_a_Project_removes_its_cursors_and_registering_it_again_starts_the_watch_from_that_day_so_what_happened_meanwhile_is_not_read()
    {
        var clock = new MovingTime(Noon);
        using var installation = await StartAsync(clock);
        var cursors = installation.Provider.GetRequiredService<IPollCursorStore>();
        await installation.Configuration.RegisterProjectAsync(101, "acme/shop", "main", Token);
        await installation.Configuration.RegisterProjectAsync(202, "acme/api", "main", Token);
        await cursors.SetAsync(101, PollStreams.IssueEvents, new PollCursor("41", null), Token);
        await cursors.SetAsync(202, PollStreams.IssueEvents, new PollCursor("9", null), Token);

        await installation.Configuration.RemoveProjectAsync(101, Token);

        Assert.Null(await cursors.GetAsync(101, PollStreams.IssueEvents, Token));
        Assert.Null(await cursors.GetAsync(101, PollStreams.Since, Token));
        Assert.Equal(new PollCursor("9", null), await cursors.GetAsync(202, PollStreams.IssueEvents, Token));

        clock.Now = Noon.AddDays(3);
        await installation.Configuration.RegisterProjectAsync(101, "acme/shop", "main", Token);

        Assert.StartsWith("2026-11-30T12:00:00", (await cursors.GetAsync(101, PollStreams.Since, Token))!.Position, StringComparison.Ordinal);
        Assert.Null(await cursors.GetAsync(101, PollStreams.IssueEvents, Token));
    }

    [Fact]
    public async Task Removing_a_Project_that_is_not_registered_changes_no_cursor()
    {
        using var installation = await StartAsync();
        var cursors = installation.Provider.GetRequiredService<IPollCursorStore>();
        await cursors.SetAsync(303, PollStreams.IssueEvents, new PollCursor("1", null), Token);

        Assert.Null(await installation.Configuration.RemoveProjectAsync(303, Token));

        Assert.NotNull(await cursors.GetAsync(303, PollStreams.IssueEvents, Token));
    }

    [Fact]
    public async Task A_Project_registered_before_polling_existed_is_watched_from_the_upgrade_and_a_removed_one_is_not()
    {
        using var installation = new TempInstallation();
        Directory.CreateDirectory(installation.Options.DataDirectory);
        await using (var context = installation.Database.OpenWriter())
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20261127010000_AddProjects", Token);
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Projects" ("RepositoryId","FullName","BaseBranch","RegisteredAt","RemovedAt") VALUES
                (1,'acme/shop','main','2026-11-20T08:00:00.0000000Z',NULL),
                (2,'acme/old','main','2026-11-20T08:00:00.0000000Z','2026-11-21T08:00:00.0000000Z')
                """,
                Token);
            await migrator.MigrateAsync(null, Token);
        }

        var cursors = installation.Provider.GetRequiredService<IPollCursorStore>();
        var since = await cursors.GetAsync(1, PollStreams.Since, Token);
        Assert.NotNull(since);
        var upgraded = DateTimeOffset.Parse(since.Position, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal);
        Assert.InRange(upgraded, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.EndsWith(".0000000Z", since.Position, StringComparison.Ordinal);
        Assert.Null(await cursors.GetAsync(2, PollStreams.Since, Token));
        Assert.Equal(2, (await installation.Reads.ListProjectsAsync(includeRemoved: true, Token)).Count);
    }

    private sealed class MovingTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
