using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Tests.Tasks;

/// <summary>NFR-5, AD-2: a Task's time in Running is what it spent before and, while it runs, the time since it entered the state; a Task that failed returns to the Queue as the same Task.</summary>
public class TaskRecordTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 28, 10, 0, 0, TimeSpan.Zero);

    private static TaskRecord Task(TaskState state, TimeSpan before = default, DateTimeOffset? since = null) =>
        new(Guid.CreateVersion7(), 101, 7, state, "hephaestus/7-x", Start, Start, before, since);

    [Fact]
    public void A_Task_that_runs_has_spent_what_it_spent_before_and_the_time_since_it_entered_Running()
    {
        var task = Task(TaskState.Running, TimeSpan.FromMinutes(20), Start);

        Assert.Equal(TimeSpan.FromMinutes(50), task.TimeInRunning(Start.AddMinutes(30)));
        Assert.Equal(TimeSpan.FromMinutes(20), task.TimeInRunning(Start));
        Assert.Equal(TimeSpan.FromMinutes(20), task.TimeInRunning(Start.AddMinutes(-5)));
    }

    [Theory]
    [InlineData(TaskState.Queued)]
    [InlineData(TaskState.WaitingForOwner)]
    [InlineData(TaskState.Failed)]
    [InlineData(TaskState.PullRequestOpen)]
    public void A_Task_that_does_not_run_spends_no_more(TaskState state)
    {
        Assert.Equal(TimeSpan.FromMinutes(20), Task(state, TimeSpan.FromMinutes(20)).TimeInRunning(Start.AddHours(5)));
    }

    [Fact]
    public void A_Task_that_runs_with_no_time_recorded_for_its_start_has_spent_only_what_it_spent_before()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), Task(TaskState.Running, TimeSpan.FromMinutes(5)).TimeInRunning(Start.AddHours(1)));
    }

    [Theory]
    [InlineData(TaskState.Failed, TaskState.Queued)]
    [InlineData(TaskState.Queued, null)]
    [InlineData(TaskState.Running, null)]
    [InlineData(TaskState.WaitingForOwner, null)]
    [InlineData(TaskState.PullRequestOpen, null)]
    [InlineData(TaskState.Cancelled, null)]
    [InlineData(TaskState.Closed, null)]
    public void Applying_the_Marker_Label_again_takes_a_Task_that_failed_back_to_the_Queue_and_no_other_Task_anywhere(TaskState state, TaskState? expected)
    {
        Assert.Equal(expected, TaskLifecycle.AfterMarkerApplied(state));
    }
}
