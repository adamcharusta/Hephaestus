using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Identity;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Workspaces;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Persistence.Tests;

/// <summary>Story 3.7, AD-5, AD-9, FR-30: the intent of a push is journaled with the Task and names the step run, the checksum, and the tips before and after; an Escalation for work that was not pushed keeps the check.</summary>
public class PushJournalTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly FixedTimeProvider Clock = new(Noon);
    private static readonly Guid StepRunId = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<(TempInstallation Installation, TaskRecord Task)> StartWithTaskAsync()
    {
        var installation = new TempInstallation(Clock);
        await installation.MigrateAsync();
        var task = new TaskRecord(EntityId.New(Clock), 101, 7, TaskState.Queued, "hephaestus/7-add-a-login-page", Noon, Noon);
        var snapshot = new IssueSnapshot("Add a login page", "Details.", IssueContent.Hash("Add a login page", "Details."), 1, Noon);
        await installation.Provider.GetRequiredService<ISignalInbox>().AppendAsync(
            [new Signal(Signal.GitHubSource, "issue-event:1", SignalKind.Labeled, 101, 7, 29, "alice", "hephaestus", null, null, Noon, "Add a login page", "Details.")],
            Token);
        await installation.Provider.GetRequiredService<IUnitOfWork>().ApplyAsync(new SignalApplication(1, SignalStatus.Applied, TaskId: task.Id, NewTask: new NewTask(task, snapshot)), Token);
        return (installation, task);
    }

    private static PushIntent Intent(Guid taskId) =>
        PushIntent.For(StepRunId, new BundleReference(BundleReference.CheckpointPath(taskId, StepRunId), new string('d', 64), new string('a', 40), new string('b', 40)), new string('c', 40));

    [Fact]
    public async Task The_intent_of_a_push_is_journaled_with_the_detail_whole_and_is_found_again_unfinished_with_its_Task()
    {
        var (installation, task) = await StartWithTaskAsync();
        using var _ = installation;
        var intent = Intent(task.Id);
        await installation.Provider.GetRequiredService<IUnitOfWork>().EscalateAsync(
            new EscalationPlan(
                new Escalation(Guid.CreateVersion7(), task.Id, EscalationKind.Decision, "q", [], ResumeConditions.TrustedReply, "s1", Noon),
                TaskState.Queued,
                [intent.ToNewEffect(task.Id)]),
            Token);

        var unfinished = Assert.Single(await installation.Reads.ListUnfinishedEffectsAsync(Token), e => e.Effect.Kind == EffectKind.Push);

        Assert.Equal(intent.Detail, unfinished.Effect.Detail);
        Assert.True(intent.Detail.Length > 128);
        Assert.Equal($"{task.Id}:{EffectKeys.StepCause(StepRunId)}:push:0", unfinished.Effect.Key);
        Assert.True(PushIntent.TryParse(unfinished.Effect.Detail, out var read));
        Assert.Equal(intent, read);
        Assert.Equal(task.Id, unfinished.Task!.Id);
    }

    [Theory]
    [InlineData(PushCheck.Secret, ResumeConditions.TrustedReply)]
    [InlineData(PushCheck.RemoteMoved, ResumeConditions.Confirm)]
    public async Task An_Escalation_for_work_that_was_not_pushed_keeps_the_check_what_it_found_and_the_resume_condition(PushCheck check, string resume)
    {
        var (installation, task) = await StartWithTaskAsync();
        using var _ = installation;
        var plan = EscalationPath.ForBlockedPush(task, check, "src/a.txt, 0123456789ab", EffectKeys.StepCause(StepRunId), Clock)!;

        Assert.True(await installation.Provider.GetRequiredService<IUnitOfWork>().EscalateAsync(plan, Token));

        var stored = Assert.Single(await installation.Reads.ListEscalationsAsync(task.Id, Token));
        Assert.Equal(plan.Escalation with { Options = [] }, stored with { Options = [] });
        Assert.Equal((EscalationKind.PushBlocked, check, "src/a.txt, 0123456789ab", resume), (stored.Kind, stored.Check, stored.Decision, stored.ResumeCondition));
        Assert.Equal(check, (await installation.Reads.FindEscalationAsync(plan.Escalation.Id, Token))!.Check);
    }

    [Fact]
    public async Task An_Escalation_that_is_no_push_has_no_check()
    {
        var (installation, task) = await StartWithTaskAsync();
        using var _ = installation;
        var plan = EscalationPath.Raise(task, EscalationRequest.For("Which?", []), "s4", Clock)!;

        await installation.Provider.GetRequiredService<IUnitOfWork>().EscalateAsync(plan, Token);

        Assert.Null(Assert.Single(await installation.Reads.ListEscalationsAsync(task.Id, Token)).Check);
    }
}
