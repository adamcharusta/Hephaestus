using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.Identity;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Persistence.Tests;

/// <summary>Story 2.9, FR-48, AD-1, AD-5: an Escalation is recorded on the Task in the transaction that moves it, and the move is made once.</summary>
public class EscalationTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly FixedTimeProvider Clock = new(Noon);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<(TempInstallation Installation, TaskRecord Task)> StartWithTaskAsync(TaskState state = TaskState.Running)
    {
        var installation = new TempInstallation(Clock);
        await installation.MigrateAsync();
        var task = new TaskRecord(EntityId.New(Clock), 101, 7, TaskState.Queued, "hephaestus/7-add-a-login-page", Noon, Noon);
        var snapshot = new IssueSnapshot("Add a login page", "Details.", IssueContent.Hash("Add a login page", "Details."), 1, Noon);
        await installation.Provider.GetRequiredService<ISignalInbox>().AppendAsync(
            [new Signal(Signal.GitHubSource, "issue-event:1", SignalKind.Labeled, 101, 7, 29, "alice", "hephaestus", null, null, Noon, "Add a login page", "Details.")],
            Token);
        var unit = installation.Provider.GetRequiredService<IUnitOfWork>();
        await unit.ApplyAsync(new SignalApplication(1, SignalStatus.Applied, TaskId: task.Id, NewTask: new NewTask(task, snapshot), NewEffects: [new NewEffect(task.Id, "g1", EffectKind.Label, "queued")]), Token);
        if (state != TaskState.Queued)
        {
            await SqlAsync(installation, $"UPDATE \"Tasks\" SET \"State\" = '{state.Tag()}'");
        }

        return (installation, task with { State = state });
    }

    private static async Task<int> SqlAsync(TempInstallation installation, string sql)
    {
        await using var context = installation.Database.OpenWriter();
        return await context.Database.ExecuteSqlRawAsync(sql, Token);
    }

    private static EscalationPlan Plan(TaskRecord task, string cause = "s4") =>
        EscalationPath.Raise(task, EscalationRequest.For("Which database?", ["SQLite", "Postgres \"quoted\""]), cause, Clock)!;

    [Fact]
    public async Task The_Escalation_is_stored_with_everything_it_asks_and_reads_back_the_same()
    {
        var (installation, task) = await StartWithTaskAsync();
        using var _ = installation;
        var plan = Plan(task);

        Assert.True(await installation.Provider.GetRequiredService<IUnitOfWork>().EscalateAsync(plan, Token));

        var stored = Assert.Single(await installation.Reads.ListEscalationsAsync(task.Id, Token));
        Assert.Equal(plan.Escalation with { Options = [] }, stored with { Options = [] });
        Assert.Equal(["SQLite", "Postgres \"quoted\""], stored.Options);
        var found = (await installation.Reads.FindEscalationAsync(plan.Escalation.Id, Token))!;
        Assert.Equal(stored with { Options = [] }, found with { Options = [] });
        Assert.Equal(stored.Options, found.Options);
        Assert.Null(await installation.Reads.FindEscalationAsync(Guid.CreateVersion7(), Token));
    }

    [Fact]
    public async Task The_Task_moves_and_the_intents_of_the_label_the_comment_and_the_notification_are_journaled_in_the_same_transaction()
    {
        var (installation, task) = await StartWithTaskAsync();
        using var _ = installation;

        await installation.Provider.GetRequiredService<IUnitOfWork>().EscalateAsync(Plan(task), Token);

        var moved = (await installation.Reads.FindTaskAsync(101, 7, Token))!;
        Assert.Equal((TaskState.WaitingForOwner, Noon), (moved.State, moved.UpdatedAt));
        var intents = (await installation.Reads.ListJournalAsync(task.Id, Token)).Where(e => e.Effect.Cause == "s4").ToList();
        Assert.Equal([$"{task.Id}:s4:label:0", $"{task.Id}:s4:comment:0", $"{task.Id}:s4:notification:0"], intents.Select(e => e.Effect.Key));
        Assert.All(intents, e => Assert.Equal(JournalEntryKind.Intent, e.Entry));
        Assert.Equal((101L, 7), (intents[1].Effect.RepositoryId, intents[1].Effect.IssueNumber));
    }

    [Theory]
    [InlineData(TaskState.WaitingForOwner)]
    [InlineData(TaskState.Closed)]
    public async Task A_Task_that_is_no_longer_where_the_plan_found_it_is_not_moved_and_nothing_is_written(TaskState now)
    {
        var (installation, task) = await StartWithTaskAsync();
        using var _ = installation;
        var plan = Plan(task);
        await SqlAsync(installation, $"UPDATE \"Tasks\" SET \"State\" = '{now.Tag()}'");

        Assert.False(await installation.Provider.GetRequiredService<IUnitOfWork>().EscalateAsync(plan, Token));

        Assert.Empty(await installation.Reads.ListEscalationsAsync(task.Id, Token));
        Assert.Single(await installation.Reads.ListJournalAsync(task.Id, Token));
        Assert.Equal(now, (await installation.Reads.FindTaskAsync(101, 7, Token))!.State);
    }

    [Fact]
    public async Task The_database_refuses_a_second_open_Escalation_for_a_Task_so_that_one_Escalation_asks_for_one_decision()
    {
        var (installation, task) = await StartWithTaskAsync();
        using var _ = installation;
        var unit = installation.Provider.GetRequiredService<IUnitOfWork>();
        await unit.EscalateAsync(Plan(task), Token);

        var insert = $"INSERT INTO \"Escalations\" (\"Id\", \"TaskId\", \"Kind\", \"Decision\", \"Options\", \"ResumeCondition\", \"Cause\", \"RaisedAt\") VALUES ('{Guid.CreateVersion7()}', '{task.Id}', 'decision', 'x', '[]', 'trusted-reply', 's9', '2026-11-27T12:00:00.0000000Z')";
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync(installation, insert));
        Assert.Contains("UNIQUE", refused.InnerException?.Message ?? refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_effect_that_could_not_be_completed_is_recorded_with_its_result_and_the_Escalation_it_raises()
    {
        var (installation, task) = await StartWithTaskAsync(TaskState.Queued);
        using var _ = installation;
        var effect = Assert.Single(await installation.Reads.ListUnfinishedEffectsAsync(Token)).Effect;
        var plan = EscalationPath.ForFailedEffect(task, effect, EffectReasons.Unreachable, Clock)!;

        Assert.True(await installation.Provider.GetRequiredService<IUnitOfWork>().RecordEffectAsync(new EffectRecording(effect, EffectResult.GaveUp, EffectReasons.Unreachable, 6, null, plan), Token));

        var stored = Assert.Single(await installation.Reads.ListEscalationsAsync(task.Id, Token));
        Assert.Equal(new FailedEffect(effect.Key, EffectKind.Label, "queued", EffectReasons.Unreachable), stored.Effect);
        Assert.Equal((EscalationKind.EffectFailed, string.Empty, "g1"), (stored.Kind, stored.Decision, stored.Cause));
        Assert.Equal(TaskState.WaitingForOwner, (await installation.Reads.FindTaskAsync(101, 7, Token))!.State);
    }

    [Fact]
    public async Task A_result_whose_Task_has_moved_meanwhile_is_still_recorded_and_the_Task_is_left_as_it_is()
    {
        var (installation, task) = await StartWithTaskAsync(TaskState.Queued);
        using var _ = installation;
        var effect = Assert.Single(await installation.Reads.ListUnfinishedEffectsAsync(Token)).Effect;
        var plan = EscalationPath.ForFailedEffect(task, effect, EffectReasons.Rejected, Clock)!;
        await SqlAsync(installation, "UPDATE \"Tasks\" SET \"State\" = 'closed'");

        Assert.True(await installation.Provider.GetRequiredService<IUnitOfWork>().RecordEffectAsync(new EffectRecording(effect, EffectResult.Refused, EffectReasons.Rejected, 0, null, plan), Token));

        Assert.Empty(await installation.Reads.ListEscalationsAsync(task.Id, Token));
        Assert.Equal(TaskState.Closed, (await installation.Reads.FindTaskAsync(101, 7, Token))!.State);
        Assert.Equal(EffectResult.Refused, (await installation.Reads.ListJournalAsync(task.Id, Token)).Single(e => e.Entry == JournalEntryKind.Result).Result);
    }

    [Theory]
    [InlineData(EscalationOutcome.Cancelled, TaskState.Cancelled)]
    [InlineData(EscalationOutcome.Closed, TaskState.Closed)]
    public async Task A_Signal_that_takes_a_waiting_Task_out_of_the_wait_moves_it_and_closes_the_Escalation_in_one_transaction(EscalationOutcome outcome, TaskState to)
    {
        var (installation, task) = await StartWithTaskAsync();
        using var _ = installation;
        var unit = installation.Provider.GetRequiredService<IUnitOfWork>();
        var plan = Plan(task);
        await unit.EscalateAsync(plan, Token);
        await installation.Provider.GetRequiredService<ISignalInbox>().AppendAsync(
            [new Signal(Signal.GitHubSource, "issue-event:2", SignalKind.Closed, 101, 7, 29, "alice", null, null, null, Noon)],
            Token);

        Assert.True(await unit.ApplyAsync(new SignalApplication(2, SignalStatus.Applied, TaskId: task.Id, MoveTaskTo: to, EndEscalation: outcome, NewEffects: [new NewEffect(task.Id, "g2", EffectKind.Label, to.Tag())]), Token));

        var moved = (await installation.Reads.FindTaskAsync(101, 7, Token))!;
        Assert.Equal((to, Noon), (moved.State, moved.UpdatedAt));
        var closed = Assert.Single(await installation.Reads.ListEscalationsAsync(task.Id, Token));
        Assert.Equal((outcome, (DateTimeOffset?)Noon), (closed.Outcome, closed.ClosedAt));
        Assert.Contains(await installation.Reads.ListJournalAsync(task.Id, Token), e => e.Effect.Key == $"{task.Id}:g2:label:0");
    }

    [Fact]
    public async Task A_Task_whose_Escalation_ended_can_raise_another_and_both_are_listed_in_the_order_they_were_raised()
    {
        var (installation, task) = await StartWithTaskAsync();
        using var _ = installation;
        var unit = installation.Provider.GetRequiredService<IUnitOfWork>();
        var first = Plan(task);
        await unit.EscalateAsync(first, Token);
        await installation.Provider.GetRequiredService<ISignalInbox>().AppendAsync(
            [new Signal(Signal.GitHubSource, "issue-event:2", SignalKind.Unlabeled, 101, 7, 29, "alice", "hephaestus", null, null, Noon)],
            Token);
        await unit.ApplyAsync(new SignalApplication(2, SignalStatus.Applied, TaskId: task.Id, MoveTaskTo: TaskState.Cancelled, EndEscalation: EscalationOutcome.Cancelled), Token);
        await SqlAsync(installation, "UPDATE \"Tasks\" SET \"State\" = 'running'");

        var second = EscalationPath.Raise(task with { State = TaskState.Running }, EscalationRequest.For("Which?", []), "s9", new FixedTimeProvider(Noon.AddMinutes(1)))!;
        Assert.True(await unit.EscalateAsync(second, Token));

        Assert.Equal([first.Escalation.Id, second.Escalation.Id], (await installation.Reads.ListEscalationsAsync(task.Id, Token)).Select(e => e.Id));
    }
}
