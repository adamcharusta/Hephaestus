using Hephaestus.Core.Effects;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>Story 3.16, AD-2, FR-9: a Trusted User who applies the Marker Label to a Task that failed returns the same Task to the Queue, and it continues on the same branch from what was pushed.</summary>
public class OrchestratorLoopOptInTests
{
    private static CancellationToken Token => RunRig.Token;

    /// <summary>A Task whose first command of Validation failed: it is Failed, its work is pushed, and its Project has no window open.</summary>
    private static async Task<(RunRig Rig, TaskRecord Failed, string Pushed)> FailedAsync()
    {
        var rig = new RunRig();
        await rig.ImplementAsync();
        await rig.TickAsync();
        var pushed = rig.Runtime.NextTip;
        await rig.EndValidationAsync(exitCode: 1);
        await rig.TickAsync();
        Assert.Equal(TaskState.Failed, rig.Task().State);
        return (rig, rig.Task(), pushed);
    }

    [Fact]
    public async Task A_Trusted_Users_Marker_Label_returns_the_same_Task_that_failed_to_the_Queue_and_the_issue_shows_it()
    {
        var (rig, failed, _) = await FailedAsync();

        await rig.Stores.AppendAsync([LoopStores.Labeled("issue-event:2")], Token);
        var report = await rig.TickAsync();

        var task = rig.Task();
        Assert.Equal((failed.Id, failed.Branch, TaskState.Queued), (task.Id, task.Branch, task.State));
        Assert.Single(rig.Stores.Tasks);
        Assert.Equal(1, report.Applied);
        Assert.Contains("hephaestus:queued", rig.Stores.Github.LabelsOf(RunRig.RepositoryId, RunRig.Issue));
        Assert.DoesNotContain("hephaestus:failed", rig.Stores.Github.LabelsOf(RunRig.RepositoryId, RunRig.Issue));
    }

    [Fact]
    public async Task The_Task_continues_on_the_same_branch_from_the_tip_last_pushed_with_a_fresh_bound_of_retries_and_its_snapshot()
    {
        var (rig, failed, pushed) = await FailedAsync();
        var snapshot = rig.Stores.TaskSnapshots[failed.Id];
        await rig.Stores.AppendAsync([LoopStores.Labeled("issue-event:2")], Token);
        await rig.TickAsync();

        await rig.StartAsync();
        await rig.TickAsync();

        var step = rig.LastStep();
        Assert.Equal((failed.Id, TaskState.Running), (rig.Task().Id, rig.Task().State));
        Assert.Equal((StepKind.Implementation, 1, pushed), (step.Kind, step.Attempt, step.TipBefore));
        Assert.Equal((failed.Branch, pushed), (rig.Runtime.Checkouts[^1].Request.Branch, rig.Runtime.Checkouts[^1].Request.Bundle.Tip));
        Assert.Same(snapshot, rig.Stores.TaskSnapshots[failed.Id]);
    }

    [Fact]
    public async Task A_Task_that_failed_after_all_its_time_was_spent_begins_again_with_none_so_that_it_can_go_on()
    {
        var rig = new RunRig(RunRig.ConfigurationFile + "\nlimits:\n  taskDuration: 1h\n");
        await rig.QueueAsync();
        await rig.StartAsync();
        await rig.TickAsync();
        rig.Time.Advance(TimeSpan.FromHours(1));
        await rig.ExitAsync(rig.LastStep().Id, 0, StepExitCause.Timeout);
        await rig.TickAsync(2);
        Assert.Equal((TaskState.Failed, TimeSpan.FromHours(1)), (rig.Task().State, rig.Task().RunningTime));

        await rig.Stores.AppendAsync([LoopStores.Labeled("issue-event:2")], Token);
        await rig.TickAsync();
        await rig.StartAsync();
        await rig.TickAsync();

        Assert.Equal((TaskState.Running, TimeSpan.Zero), (rig.Task().State, rig.Task().RunningTime));
        Assert.Equal(rig.Time.Now + TimeSpan.FromHours(1), rig.LastStep().Deadline);
    }

    [Fact]
    public async Task A_label_applied_by_someone_who_is_not_a_Trusted_User_changes_nothing_on_a_Task_that_failed()
    {
        var (rig, _, _) = await FailedAsync();

        await rig.Stores.AppendAsync([LoopStores.Labeled("issue-event:2", actor: 99, login: "mallory")], Token);
        var report = await rig.TickAsync();

        Assert.Equal((TaskState.Failed, 1), (rig.Task().State, report.Rejected));
    }

    [Theory]
    [InlineData(TaskState.Queued)]
    [InlineData(TaskState.PullRequestOpen)]
    [InlineData(TaskState.Cancelled)]
    public async Task The_label_applied_again_changes_nothing_on_a_Task_in_any_other_state_and_makes_no_second_Task(TaskState state)
    {
        var rig = new RunRig();
        await rig.QueueAsync();
        rig.Stores.Tasks[0] = rig.Stores.Tasks[0] with { State = state };

        await rig.Stores.AppendAsync([LoopStores.Labeled("issue-event:2")], Token);
        await rig.TickAsync();

        Assert.Equal((state, 1), (rig.Task().State, rig.Stores.Tasks.Count));
        Assert.DoesNotContain(rig.Stores.Journal, entry => entry.Entry == JournalEntryKind.Intent && entry.Effect.Kind == EffectKind.Label && entry.Effect.Cause == EffectKeys.SignalCause(2));
    }
}
