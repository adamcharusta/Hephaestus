using Hephaestus.Core.Identity;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Persistence.Tests;

/// <summary>Story 2.2, AD-1: one transaction per Signal marks it and writes the Task it causes, and only a pending Signal is applied.</summary>
public class TaskTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<TempInstallation> StartAsync(TimeProvider? time = null)
    {
        var installation = new TempInstallation(time ?? new FixedTimeProvider(Noon));
        await installation.MigrateAsync();
        return installation;
    }

    private static Signal Labeled(string id, int issue = 7, string title = "Add a login page", string? body = "Details.") =>
        new(Signal.GitHubSource, id, SignalKind.Labeled, 101, issue, 29, "alice", "hephaestus", null, null, Noon, title, body);

    private static TaskRecord NewTask(int issue = 7) =>
        new(EntityId.New(TimeProvider.System), 101, issue, TaskState.Queued, $"hephaestus/{issue}-add-a-login-page", Noon, Noon);

    private static IssueSnapshot Snapshot(long sequence = 1) =>
        new("Add a login page", "Details.", IssueContent.Hash("Add a login page", "Details."), sequence, Noon);

    [Fact]
    public async Task A_new_inbox_signal_is_pending_and_is_read_as_pending_in_inbox_order()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();
        await inbox.AppendAsync([Labeled("issue-event:2"), Labeled("issue-event:1", issue: 8)], Token);

        var pending = await inbox.ReadPendingAsync(10, Token);

        Assert.Equal(["issue-event:2", "issue-event:1"], pending.Select(s => s.Signal.SourceEventId));
        Assert.All(pending, stored => Assert.Equal((SignalStatus.Pending, null), (stored.Status, stored.Reason)));
        Assert.Single(await inbox.ReadPendingAsync(1, Token));
    }

    [Fact]
    public async Task The_title_and_body_of_a_labelled_event_are_stored_with_it_and_read_back()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();

        await inbox.AppendAsync([Labeled("issue-event:1", title: "Täst 😀", body: "line\r\nline")], Token);

        var signal = Assert.Single(await inbox.ReadAsync(0, 10, Token)).Signal;
        Assert.Equal("Täst 😀", signal.IssueTitle);
        Assert.Equal("line\r\nline", signal.IssueBody);
    }

    [Fact]
    public async Task Applying_a_Signal_marks_it_and_writes_the_Task_and_its_snapshot_together()
    {
        using var installation = await StartAsync(new FixedTimeProvider(Noon.AddMinutes(3)));
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();
        await inbox.AppendAsync([Labeled("issue-event:1")], Token);
        var task = NewTask();

        var applied = await installation.Provider.GetRequiredService<IUnitOfWork>().ApplyAsync(
            new SignalApplication(1, SignalStatus.Applied, TaskId: task.Id, NewTask: new NewTask(task, Snapshot())), Token);

        Assert.True(applied);
        var stored = Assert.Single(await inbox.ReadAsync(0, 10, Token));
        Assert.Equal((SignalStatus.Applied, null, task.Id), (stored.Status, stored.Reason, stored.TaskId));
        Assert.Empty(await inbox.ReadPendingAsync(10, Token));
        Assert.Equal(task, await installation.Reads.FindTaskAsync(101, 7, Token));
        Assert.Equal(Snapshot(), await installation.Reads.FindIssueSnapshotAsync(task.Id, Token));
        await using var context = installation.Database.OpenReader();
        Assert.Equal(Noon.AddMinutes(3), (await context.Signals.SingleAsync(Token)).ProcessedAt);
    }

    [Fact]
    public async Task A_rejected_Signal_keeps_its_reason_and_writes_no_Task()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();
        await inbox.AppendAsync([Labeled("issue-event:1")], Token);

        await installation.Provider.GetRequiredService<IUnitOfWork>().ApplyAsync(new SignalApplication(1, SignalStatus.Rejected, SignalRejections.ActorNotTrusted), Token);

        var stored = Assert.Single(await inbox.ReadAsync(0, 10, Token));
        Assert.Equal((SignalStatus.Rejected, SignalRejections.ActorNotTrusted, null), (stored.Status, stored.Reason, stored.TaskId));
        Assert.Null(await installation.Reads.FindTaskAsync(101, 7, Token));
    }

    [Fact]
    public async Task A_Signal_that_concerned_an_existing_Task_names_it_and_writes_nothing_else()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();
        var unit = installation.Provider.GetRequiredService<IUnitOfWork>();
        await inbox.AppendAsync([Labeled("issue-event:1"), Labeled("issue-event:2")], Token);
        var task = NewTask();
        await unit.ApplyAsync(new SignalApplication(1, SignalStatus.Applied, TaskId: task.Id, NewTask: new NewTask(task, Snapshot())), Token);

        await unit.ApplyAsync(new SignalApplication(2, SignalStatus.Applied, TaskId: task.Id), Token);

        Assert.Equal(task.Id, (await inbox.ReadAsync(1, 10, Token)).Single().TaskId);
        await using var context = installation.Database.OpenReader();
        Assert.Equal(1, await context.Tasks.CountAsync(Token));
        Assert.Equal(1, await context.IssueSnapshots.CountAsync(Token));
    }

    [Fact]
    public async Task A_Signal_that_is_no_longer_pending_or_does_not_exist_is_not_applied_and_nothing_is_written()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();
        var unit = installation.Provider.GetRequiredService<IUnitOfWork>();
        await inbox.AppendAsync([Labeled("issue-event:1")], Token);
        await unit.ApplyAsync(new SignalApplication(1, SignalStatus.Rejected, SignalRejections.ActorNotTrusted), Token);
        var task = NewTask();

        Assert.False(await unit.ApplyAsync(new SignalApplication(1, SignalStatus.Applied, TaskId: task.Id, NewTask: new NewTask(task, Snapshot())), Token));
        Assert.False(await unit.ApplyAsync(new SignalApplication(99, SignalStatus.Applied), Token));

        Assert.Null(await installation.Reads.FindTaskAsync(101, 7, Token));
        Assert.Equal(SignalStatus.Rejected, Assert.Single(await inbox.ReadAsync(0, 10, Token)).Status);
    }

    [Fact]
    public async Task A_Signal_cannot_be_left_pending_by_applying_it()
    {
        using var installation = await StartAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => installation.Provider.GetRequiredService<IUnitOfWork>().ApplyAsync(new SignalApplication(1, SignalStatus.Pending), Token));
    }

    [Fact]
    public async Task A_second_Task_for_the_same_issue_is_refused_and_the_Signal_stays_pending()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();
        var unit = installation.Provider.GetRequiredService<IUnitOfWork>();
        await inbox.AppendAsync([Labeled("issue-event:1"), Labeled("issue-event:2")], Token);
        var first = NewTask();
        await unit.ApplyAsync(new SignalApplication(1, SignalStatus.Applied, TaskId: first.Id, NewTask: new NewTask(first, Snapshot())), Token);
        var second = NewTask();

        await Assert.ThrowsAsync<DbUpdateException>(() => unit.ApplyAsync(new SignalApplication(2, SignalStatus.Applied, TaskId: second.Id, NewTask: new NewTask(second, Snapshot(2))), Token));

        Assert.Equal(SignalStatus.Pending, (await inbox.ReadAsync(1, 10, Token)).Single().Status);
        Assert.Equal(first, await installation.Reads.FindTaskAsync(101, 7, Token));
        await using var context = installation.Database.OpenReader();
        Assert.Equal(1, await context.IssueSnapshots.CountAsync(Token));
    }

    [Fact]
    public async Task A_Task_is_found_by_Project_and_issue_number_only()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();
        await inbox.AppendAsync([Labeled("issue-event:1")], Token);
        var task = NewTask();
        await installation.Provider.GetRequiredService<IUnitOfWork>().ApplyAsync(new SignalApplication(1, SignalStatus.Applied, TaskId: task.Id, NewTask: new NewTask(task, Snapshot())), Token);

        Assert.NotNull(await installation.Reads.FindTaskAsync(101, 7, Token));
        Assert.Null(await installation.Reads.FindTaskAsync(101, 8, Token));
        Assert.Null(await installation.Reads.FindTaskAsync(102, 7, Token));
        Assert.Null(await installation.Reads.FindIssueSnapshotAsync(EntityId.New(TimeProvider.System), Token));
    }

    [Fact]
    public async Task The_newest_snapshot_of_a_Task_is_the_one_read()
    {
        using var installation = await StartAsync();
        var task = NewTask();
        await using (var context = installation.Database.OpenWriter())
        {
            context.Tasks.Add(new Model.TaskEntry { Id = task.Id, RepositoryId = 101, IssueNumber = 7, State = "queued", Branch = task.Branch, CreatedAt = Noon, UpdatedAt = Noon });
            context.IssueSnapshots.Add(new Model.IssueSnapshotEntry { TaskId = task.Id, Title = "Old", Body = "", Hash = "a", SignalSequence = 1, TakenAt = Noon });
            context.IssueSnapshots.Add(new Model.IssueSnapshotEntry { TaskId = task.Id, Title = "New", Body = "b", Hash = "b", SignalSequence = 5, TakenAt = Noon.AddHours(1) });
            await context.SaveChangesAsync(Token);
        }

        var snapshot = await installation.Reads.FindIssueSnapshotAsync(task.Id, Token);

        Assert.Equal(new IssueSnapshot("New", "b", "b", 5, Noon.AddHours(1)), snapshot);
    }

    [Fact]
    public async Task A_stored_Task_with_a_state_that_is_no_state_is_a_fault_not_a_guess()
    {
        using var installation = await StartAsync();
        await using (var context = installation.Database.OpenWriter())
        {
            context.Tasks.Add(new Model.TaskEntry { Id = EntityId.New(TimeProvider.System), RepositoryId = 101, IssueNumber = 7, State = "sleeping", Branch = "hephaestus/7", CreatedAt = Noon, UpdatedAt = Noon });
            await context.SaveChangesAsync(Token);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => installation.Reads.FindTaskAsync(101, 7, Token));
    }

    [Fact]
    public async Task A_stored_Signal_with_an_unknown_status_is_a_fault_not_a_guess()
    {
        using var installation = await StartAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();
        await inbox.AppendAsync([Labeled("issue-event:1")], Token);
        await using (var context = installation.Database.OpenWriter())
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE \"Signals\" SET \"Status\" = 'lost'", Token);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => inbox.ReadAsync(0, 10, Token));
    }

    [Fact]
    public async Task The_App_slug_is_stored_with_its_id_and_replaced_with_it()
    {
        using var installation = await StartAsync();
        Assert.Null(await installation.Reads.GetGitHubAppSlugAsync(Token));

        await installation.Configuration.SetGitHubAppIdAsync(42, "hephaestus-bot", Token);
        Assert.Equal("hephaestus-bot", await installation.Reads.GetGitHubAppSlugAsync(Token));

        await installation.Configuration.SetGitHubAppIdAsync(43, "other-app", Token);
        Assert.Equal(("other-app", 43L), (await installation.Reads.GetGitHubAppSlugAsync(Token), await installation.Reads.GetGitHubAppIdAsync(Token)));
    }

    [Fact]
    public async Task Signals_and_an_App_connection_of_the_release_before_are_kept_the_Signals_pending_and_the_slug_unknown()
    {
        using var installation = new TempInstallation();
        Directory.CreateDirectory(installation.Options.DataDirectory);
        await using (var context = installation.Database.OpenWriter())
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20261127100000_AddSignalInbox", Token);
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "GitHubAppConnections" ("Id","AppId","ConnectedAt") VALUES (1,7,'2026-11-20T08:00:00.0000000Z');
                INSERT INTO "Signals" ("Source","SourceEventId","Kind","RepositoryId","IssueNumber","ActorId","ActorLogin","Label","OccurredAt")
                VALUES ('github','issue-event:1','labeled',101,7,29,'alice','hephaestus','2026-11-27T12:00:00.0000000Z')
                """,
                Token);
            await migrator.MigrateAsync(null, Token);
        }

        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();
        var pending = Assert.Single(await inbox.ReadPendingAsync(10, Token));
        Assert.Equal((SignalStatus.Pending, null), (pending.Status, pending.Signal.IssueTitle));
        Assert.Null(await installation.Reads.GetGitHubAppSlugAsync(Token));
        Assert.Equal(7, await installation.Reads.GetGitHubAppIdAsync(Token));
    }
}
