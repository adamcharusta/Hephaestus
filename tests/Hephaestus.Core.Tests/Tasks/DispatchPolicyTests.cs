using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Tests.Tasks;

/// <summary>FR-48: a Task that waits for the Owner takes no part in dispatch and holds no other Task back.</summary>
public class DispatchPolicyTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static TaskRecord Task(int issue, TaskState state) => new(Guid.CreateVersion7(), 101, issue, state, $"hephaestus/{issue}-x", Noon, Noon);

    [Fact]
    public void A_Task_waiting_for_the_Owner_does_not_hold_back_the_Tasks_that_are_queued()
    {
        var waiting = Task(3, TaskState.WaitingForOwner);
        var first = Task(5, TaskState.Queued);
        var second = Task(9, TaskState.Queued);

        Assert.Equal([first, second], DispatchPolicy.Eligible([waiting, first, second]));
    }

    [Theory]
    [InlineData(TaskState.Running)]
    [InlineData(TaskState.WaitingForOwner)]
    [InlineData(TaskState.WaitingForCapacity)]
    [InlineData(TaskState.PullRequestOpen)]
    [InlineData(TaskState.Failed)]
    [InlineData(TaskState.Cancelled)]
    [InlineData(TaskState.Withdrawn)]
    [InlineData(TaskState.Merged)]
    [InlineData(TaskState.Closed)]
    public void Only_a_queued_Task_is_eligible(TaskState state) => Assert.Empty(DispatchPolicy.Eligible([Task(1, state)]));

    [Fact]
    public void Nothing_eligible_when_nothing_is_queued() => Assert.Empty(DispatchPolicy.Eligible([]));

    [Theory]
    [InlineData(TaskState.WaitingForOwner, TaskState.Cancelled, TaskState.Closed)]
    public void The_lifecycle_rows_of_a_waiting_Task_take_it_to_Cancelled_by_a_removal_and_to_Closed_by_a_close(TaskState from, TaskState removed, TaskState closed)
    {
        Assert.Equal(removed, TaskLifecycle.AfterMarkerRemoved(from));
        Assert.Equal(closed, TaskLifecycle.AfterIssueClosed(from));
    }

    [Theory]
    [InlineData(TaskState.Queued)]
    [InlineData(TaskState.Running)]
    [InlineData(TaskState.Merged)]
    [InlineData(TaskState.Closed)]
    public void The_rows_of_the_other_states_belong_to_Story_2_3_and_take_a_Task_nowhere_yet(TaskState from)
    {
        Assert.Null(TaskLifecycle.AfterMarkerRemoved(from));
        Assert.Null(TaskLifecycle.AfterIssueClosed(from));
    }
}
