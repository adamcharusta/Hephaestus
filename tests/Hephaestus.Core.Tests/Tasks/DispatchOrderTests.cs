using Hephaestus.Core.Tasks;
using Hephaestus.Core.Windows;

namespace Hephaestus.Core.Tests.Tasks;

/// <summary>Story 3.14, FR-14, AD-1: which Tasks may be dispatched, and in which order. Only a Queued Task of a Project with an open window, and the order is the issue number alone; that no second Task runs is the unit of work's rule (<c>DispatchPersistenceTests</c>).</summary>
public class DispatchOrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);

    private static TaskRecord Task(int issue, TaskState state = TaskState.Queued, long repository = 1) =>
        new(Guid.CreateVersion7(), repository, issue, state, $"hephaestus/{issue}", Now, Now);

    private static Window Open(long repository = 1) => new(Guid.CreateVersion7(), repository, Now);

    private static IReadOnlySet<long> Windows(params long[] repositories) => repositories.ToHashSet();

    [Fact]
    public void The_Task_with_the_lowest_issue_number_is_first()
    {
        var tasks = new[] { Task(9), Task(3), Task(5) };

        Assert.Equal([3, 5, 9], DispatchPolicy.InWindows(tasks, Windows(1)).Select(task => task.IssueNumber));
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
    public void A_Task_that_is_not_Queued_is_never_in_the_order_and_holds_no_other_back(TaskState state)
    {
        var tasks = new[] { Task(1, state), Task(2) };

        Assert.Equal([2], DispatchPolicy.InWindows(tasks, Windows(1)).Select(task => task.IssueNumber));
    }

    [Fact]
    public void Only_a_Task_of_a_Project_with_an_open_window_is_in_the_order()
    {
        var tasks = new[] { Task(1, repository: 1), Task(2, repository: 2) };

        Assert.Equal([2], DispatchPolicy.InWindows(tasks, Windows(2)).Select(task => task.IssueNumber));
        Assert.Empty(DispatchPolicy.InWindows(tasks, Windows()));
    }

    [Fact]
    public void The_order_is_the_issue_number_across_Projects_and_then_the_repository_so_that_it_is_the_same_on_every_tick()
    {
        var tasks = new[] { Task(4, repository: 2), Task(4, repository: 1), Task(2, repository: 3) };

        var order = DispatchPolicy.InWindows(tasks, new HashSet<long> { 1, 2, 3 });

        Assert.Equal([(2, 3L), (4, 1L), (4, 2L)], order.Select(task => (task.IssueNumber, task.RepositoryId)));
    }

    [Fact]
    public void A_window_is_drained_when_no_Task_of_its_Project_is_Queued_whatever_else_is_running()
    {
        var tasks = new[] { Task(1, TaskState.Running, repository: 1), Task(2, repository: 2) };
        var windows = new[] { Open(1), Open(2) };

        var drained = DispatchPolicy.Drained(tasks, windows);

        Assert.Equal([windows[0]], drained);
    }

    [Fact]
    public void A_window_that_is_closed_is_never_drained_again()
    {
        var closed = Open(1) with { ClosedAt = Now, End = WindowEnd.Stopped };

        Assert.Empty(DispatchPolicy.Drained([], [closed]));
    }
}
