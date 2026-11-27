using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.Identity;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Persistence.Tests;

/// <summary>Story 2.10, FR-49, AD-1, AD-15: a comment of a Trusted User is admitted in the transaction that applies its Signal, and an answer returns the Task to the Queue in the same one.</summary>
public class AdmittedCommentTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly FixedTimeProvider Clock = new(Noon);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<(TempInstallation Installation, TaskRecord Task)> StartWithWaitingTaskAsync()
    {
        var installation = new TempInstallation(Clock);
        await installation.MigrateAsync();
        var task = new TaskRecord(EntityId.New(Clock), 101, 7, TaskState.Queued, "hephaestus/7-add-a-login-page", Noon, Noon);
        var snapshot = new IssueSnapshot("Add a login page", "Details.", IssueContent.Hash("Add a login page", "Details."), 1, Noon);
        await installation.Provider.GetRequiredService<ISignalInbox>().AppendAsync(
            [new Signal(Signal.GitHubSource, "issue-event:1", SignalKind.Labeled, 101, 7, 29, "alice", "hephaestus", null, null, Noon, "Add a login page", "Details.")],
            Token);
        var unit = installation.Provider.GetRequiredService<IUnitOfWork>();
        await unit.ApplyAsync(new SignalApplication(1, SignalStatus.Applied, TaskId: task.Id, NewTask: new NewTask(task, snapshot)), Token);
        await SqlAsync(installation, "UPDATE \"Tasks\" SET \"State\" = 'running'");
        var plan = EscalationPath.Raise(task with { State = TaskState.Running }, EscalationRequest.For("Which database?", ["SQLite", "Postgres"]), "s4", Clock)!;
        await unit.EscalateAsync(plan, Token);
        return (installation, task with { State = TaskState.WaitingForOwner });
    }

    private static async Task SqlAsync(TempInstallation installation, string sql)
    {
        await using var context = installation.Database.OpenWriter();
        await context.Database.ExecuteSqlRawAsync(sql, Token);
    }

    private static Signal CommentSignal(string id, string? body, long commentId = 500) =>
        new(Signal.GitHubSource, id, SignalKind.CommentCreated, 101, 7, 29, "alice", null, commentId, 29, Noon.AddMinutes(1), CommentBody: body);

    [Fact]
    public async Task The_text_of_a_created_comment_is_kept_on_its_Signal_and_the_text_of_another_kind_is_null()
    {
        using var installation = new TempInstallation(Clock);
        await installation.MigrateAsync();
        var inbox = installation.Provider.GetRequiredService<ISignalInbox>();

        await inbox.AppendAsync(
            [CommentSignal("comment:500:created", "Use Postgres.\r\nThanks."), new Signal(Signal.GitHubSource, "comment:9:edited:x", SignalKind.CommentEdited, 101, 7, null, null, null, 9, 30, Noon)],
            Token);

        var stored = await inbox.ReadAsync(0, 10, Token);
        Assert.Equal(["Use Postgres.\r\nThanks.", null], stored.Select(s => s.Signal.CommentBody));
        Assert.Equal(CommentSignal("comment:500:created", "Use Postgres.\r\nThanks."), stored[0].Signal);
    }

    [Fact]
    public async Task An_answer_admits_the_comment_returns_the_Task_to_Queued_and_closes_the_Escalation_in_one_transaction()
    {
        var (installation, task) = await StartWithWaitingTaskAsync();
        using var _ = installation;
        var escalation = Assert.Single(await installation.Reads.ListEscalationsAsync(task.Id, Token));
        await installation.Provider.GetRequiredService<ISignalInbox>().AppendAsync([CommentSignal("comment:500:created", "Use Postgres.")], Token);
        var comment = new AdmittedComment(task.Id, 500, 29, "alice", "Use Postgres.", CommentContent.Hash("Use Postgres."), 2, Noon.AddMinutes(1), escalation.Id);

        Assert.True(await installation.Provider.GetRequiredService<IUnitOfWork>().ApplyAsync(
            new SignalApplication(2, SignalStatus.Applied, TaskId: task.Id, MoveTaskTo: TaskState.Queued, EndEscalation: EscalationOutcome.Answered, AdmitComment: comment, NewEffects: [new NewEffect(task.Id, "s2", EffectKind.Label, "queued")]),
            Token));

        Assert.Equal(TaskState.Queued, (await installation.Reads.FindTaskAsync(101, 7, Token))!.State);
        Assert.Equal([comment], await installation.Reads.ListAdmittedCommentsAsync(task.Id, Token));
        var closed = Assert.Single(await installation.Reads.ListEscalationsAsync(task.Id, Token));
        Assert.Equal((EscalationOutcome.Answered, (DateTimeOffset?)Noon), (closed.Outcome, closed.ClosedAt));
        Assert.Contains(await installation.Reads.ListJournalAsync(task.Id, Token), e => e.Effect.Key == $"{task.Id}:s2:label:0");
    }

    [Fact]
    public async Task A_comment_that_is_context_only_is_listed_oldest_first_with_no_Escalation_and_changes_no_state()
    {
        var (installation, task) = await StartWithWaitingTaskAsync();
        using var _ = installation;
        await installation.Provider.GetRequiredService<ISignalInbox>().AppendAsync([CommentSignal("comment:500:created", "First."), CommentSignal("comment:501:created", "Second.", 501)], Token);
        var unit = installation.Provider.GetRequiredService<IUnitOfWork>();

        await unit.ApplyAsync(new SignalApplication(3, SignalStatus.Applied, TaskId: task.Id, AdmitComment: new AdmittedComment(task.Id, 501, 29, null, "Second.", CommentContent.Hash("Second."), 3, Noon)), Token);
        await unit.ApplyAsync(new SignalApplication(2, SignalStatus.Applied, TaskId: task.Id, AdmitComment: new AdmittedComment(task.Id, 500, 29, "alice", "First.", CommentContent.Hash("First."), 2, Noon)), Token);

        var listed = await installation.Reads.ListAdmittedCommentsAsync(task.Id, Token);
        Assert.Equal([500L, 501L], listed.Select(c => c.CommentId));
        Assert.All(listed, c => Assert.Null(c.AnswersEscalationId));
        Assert.Equal(TaskState.WaitingForOwner, (await installation.Reads.FindTaskAsync(101, 7, Token))!.State);
        Assert.Null(Assert.Single(await installation.Reads.ListEscalationsAsync(task.Id, Token)).ClosedAt);
        Assert.Empty(await installation.Reads.ListAdmittedCommentsAsync(Guid.CreateVersion7(), Token));
    }

    [Fact]
    public async Task The_database_admits_a_comment_for_a_Task_once()
    {
        var (installation, task) = await StartWithWaitingTaskAsync();
        using var _ = installation;
        await installation.Provider.GetRequiredService<ISignalInbox>().AppendAsync([CommentSignal("comment:500:created", "First."), CommentSignal("comment:500:edited", "Again.")], Token);
        var unit = installation.Provider.GetRequiredService<IUnitOfWork>();
        await unit.ApplyAsync(new SignalApplication(2, SignalStatus.Applied, TaskId: task.Id, AdmitComment: new AdmittedComment(task.Id, 500, 29, "alice", "First.", CommentContent.Hash("First."), 2, Noon)), Token);

        await Assert.ThrowsAnyAsync<Exception>(() => unit.ApplyAsync(
            new SignalApplication(3, SignalStatus.Applied, TaskId: task.Id, AdmitComment: new AdmittedComment(task.Id, 500, 29, "alice", "Again.", CommentContent.Hash("Again."), 3, Noon)),
            Token));

        Assert.Equal(["First."], (await installation.Reads.ListAdmittedCommentsAsync(task.Id, Token)).Select(c => c.Body));
        Assert.Equal(SignalStatus.Pending, (await installation.Provider.GetRequiredService<ISignalInbox>().ReadPendingAsync(10, Token)).Single(s => s.Sequence == 3).Status);
    }
}
