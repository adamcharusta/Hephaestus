using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Windows;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>
/// Story 3.14, FR-16, AD-1, AD-17: the Owner's request to start work on a Project is a Signal the loop applies to a window record; a second start opens no second window and says so; a stop closes it; a window ends when its Project has no Task
/// left to work; and a Task that is running finishes whatever happens to the window. The status of a Signal can be read as pending, applied, or rejected.
/// </summary>
public class OrchestratorLoopWindowTests
{
    private static CancellationToken Token => RunRig.Token;

    /// <summary>A rig whose Project has no configuration file, so that a Task queued in it cannot start and its window stays open while it waits.</summary>
    private static RunRig WithATaskThatCannotStart() => new(configuration: null);

    [Fact]
    public async Task A_start_is_pending_until_the_loop_applies_it_and_then_opens_one_window_for_the_Project()
    {
        var rig = WithATaskThatCannotStart();
        await rig.QueueAsync();
        var id = await rig.StartAsync();

        Assert.Equal(SignalStatus.Pending, (await rig.Stores.FindAsync(id, Token))!.Status);
        await rig.TickAsync();

        var applied = (await rig.Stores.FindAsync(id, Token))!;
        Assert.Equal((SignalStatus.Applied, null), (applied.Status, applied.Reason));
        var window = Assert.Single(rig.Stores.Windows);
        Assert.Equal((RunRig.RepositoryId, true, null), (window.RepositoryId, window.IsOpen, window.End));
    }

    [Fact]
    public async Task A_second_start_is_applied_without_opening_a_second_window_and_the_note_says_so()
    {
        var rig = WithATaskThatCannotStart();
        await rig.QueueAsync();
        await rig.StartAsync();
        await rig.TickAsync();

        var second = await rig.StartAsync();
        await rig.TickAsync();

        var stored = (await rig.Stores.FindAsync(second, Token))!;
        Assert.Equal((SignalStatus.Applied, SignalNotes.WindowAlreadyOpen), (stored.Status, stored.Reason));
        Assert.Single(rig.Stores.Windows);
    }

    [Fact]
    public async Task Two_starts_in_one_batch_open_one_window_and_the_second_is_the_one_that_is_noted()
    {
        var rig = WithATaskThatCannotStart();
        await rig.QueueAsync();
        var first = await rig.StartAsync();
        var second = await rig.StartAsync();

        await rig.TickAsync();

        Assert.Single(rig.Stores.Windows);
        Assert.Null((await rig.Stores.FindAsync(first, Token))!.Reason);
        Assert.Equal(SignalNotes.WindowAlreadyOpen, (await rig.Stores.FindAsync(second, Token))!.Reason);
    }

    [Fact]
    public async Task A_stop_closes_the_window_as_stopped_and_a_stop_with_no_window_open_is_applied_with_the_note_that_says_so()
    {
        var rig = WithATaskThatCannotStart();
        await rig.QueueAsync();
        await rig.StartAsync();
        await rig.TickAsync();

        var stop = await rig.StopAsync();
        var again = await rig.StopAsync();
        await rig.TickAsync();

        var window = Assert.Single(rig.Stores.Windows);
        Assert.Equal((false, WindowEnd.Stopped), (window.IsOpen, window.End));
        Assert.Null((await rig.Stores.FindAsync(stop, Token))!.Reason);
        Assert.Equal((SignalStatus.Applied, SignalNotes.WindowNotOpen), ((await rig.Stores.FindAsync(again, Token))!.Status, (await rig.Stores.FindAsync(again, Token))!.Reason));
    }

    [Fact]
    public async Task A_request_for_a_Project_that_is_not_registered_is_rejected_and_opens_nothing()
    {
        var rig = WithATaskThatCannotStart();

        var start = await rig.StartAsync(repositoryId: 999);
        var stop = await rig.StopAsync(repositoryId: 999);
        await rig.TickAsync();

        foreach (var id in new[] { start, stop })
        {
            var stored = (await rig.Stores.FindAsync(id, Token))!;
            Assert.Equal((SignalStatus.Rejected, SignalRejections.ProjectNotRegistered), (stored.Status, stored.Reason));
        }

        Assert.Empty(rig.Stores.Windows);
    }

    [Fact]
    public async Task A_window_on_a_Project_with_no_Task_to_work_ends_as_drained_in_the_tick_that_opened_it()
    {
        var rig = WithATaskThatCannotStart();

        await rig.StartAsync();
        await rig.TickAsync();

        var window = Assert.Single(rig.Stores.Windows);
        Assert.Equal((false, WindowEnd.Drained), (window.IsOpen, window.End));
    }

    [Fact]
    public async Task A_window_stays_open_while_a_Task_of_the_Project_waits_in_the_Queue()
    {
        var rig = WithATaskThatCannotStart();
        await rig.QueueAsync();
        await rig.StartAsync();

        await rig.TickAsync(3);

        Assert.True(Assert.Single(rig.Stores.Windows).IsOpen);
        Assert.Equal(TaskState.Queued, rig.Task().State);
    }

    [Fact]
    public async Task A_window_that_is_stopped_while_a_Task_runs_lets_that_Task_finish_and_starts_no_other()
    {
        var rig = new RunRig();
        await rig.QueueAsync(issue: 3);
        await rig.QueueAsync(eventId: "issue-event:2", issue: 9);
        await rig.StartAsync();
        await rig.TickAsync();
        await rig.StopAsync();
        await rig.TickAsync();

        Assert.Equal((TaskState.Running, TaskState.Queued), (rig.Task(3).State, rig.Task(9).State));
        await rig.ExitAsync(rig.LastStep(3).Id, 0, Hephaestus.Core.Workspaces.StepExitCause.Exited, RunRig.Completed());
        await rig.TickAsync(2);
        await rig.EndValidationAsync(issue: 3);
        await rig.EndValidationAsync(issue: 3);
        await rig.TickAsync(3);

        Assert.Equal((TaskState.PullRequestOpen, TaskState.Queued), (rig.Task(3).State, rig.Task(9).State));
        Assert.Empty(rig.Steps(9));
    }

    [Fact]
    public async Task A_window_of_a_Project_the_Owner_removed_is_closed_though_Tasks_wait_in_its_Queue()
    {
        var rig = WithATaskThatCannotStart();
        await rig.QueueAsync();
        await rig.StartAsync();
        await rig.TickAsync();
        Assert.True(Assert.Single(rig.Stores.Windows).IsOpen);

        rig.Stores.Projects[0] = rig.Stores.Projects[0] with { RemovedAt = rig.Time.Now };
        await rig.TickAsync();

        Assert.Equal(WindowEnd.Stopped, Assert.Single(rig.Stores.Windows).End);
    }

    [Fact]
    public async Task A_window_ends_as_drained_when_the_last_Queued_Task_has_been_started_and_the_Task_that_runs_finishes_all_the_same()
    {
        var rig = new RunRig();
        await rig.QueueAsync();
        await rig.StartAsync();

        await rig.TickAsync();

        Assert.Equal(TaskState.Running, rig.Task().State);
        Assert.Equal(WindowEnd.Drained, Assert.Single(rig.Stores.Windows).End);
    }
}
