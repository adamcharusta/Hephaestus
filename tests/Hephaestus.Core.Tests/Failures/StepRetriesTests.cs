using Hephaestus.Core.Failures;

namespace Hephaestus.Core.Tests.Failures;

/// <summary>AD-11, NFR-10, NFR-5: a failed step is attempted again up to the bound while the Task has time left; otherwise the Task ends, and the end says why.</summary>
public class StepRetriesTests
{
    private static readonly TimeSpan Plenty = TimeSpan.FromHours(1);

    [Theory]
    [InlineData(1, 2, StepFailureEnd.Retry)]
    [InlineData(2, 2, StepFailureEnd.Retry)]
    [InlineData(3, 2, StepFailureEnd.Exhausted)]
    [InlineData(1, 0, StepFailureEnd.Exhausted)]
    [InlineData(1, 1, StepFailureEnd.Retry)]
    [InlineData(2, 1, StepFailureEnd.Exhausted)]
    public void A_step_is_attempted_again_up_to_the_bound_so_its_attempts_are_the_bound_and_one(int attempt, int bound, StepFailureEnd expected)
    {
        Assert.Equal(expected, StepRetries.Decide(FailureClass.Unknown, attempt, bound, Plenty));
    }

    [Theory]
    [InlineData(FailureClass.ValidationFailure)]
    [InlineData(FailureClass.UsageLimit)]
    [InlineData(FailureClass.Cancelled)]
    public void A_class_that_does_not_spend_the_bound_ends_the_Task_whatever_is_left_of_it(FailureClass failure)
    {
        Assert.Equal(StepFailureEnd.NotRetried, StepRetries.Decide(failure, 1, 10, Plenty));
        Assert.Equal(StepFailureEnd.NotRetried, StepRetries.Decide(failure, 1, 10, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1000)]
    public void A_Task_with_no_time_left_in_Running_attempts_nothing_again_even_with_retries_left(int milliseconds)
    {
        Assert.Equal(StepFailureEnd.DurationLimit, StepRetries.Decide(FailureClass.Timeout, 1, 5, TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void A_Task_with_more_than_the_slack_left_is_attempted_again()
    {
        Assert.Equal(StepFailureEnd.Retry, StepRetries.Decide(FailureClass.Timeout, 1, 5, StepRetries.DurationSlack + TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void The_end_of_a_Task_has_a_name_and_a_retry_has_none()
    {
        Assert.Equal(["not-retried", "exhausted", "duration-limit", "no-change"], new[] { StepFailureEnd.NotRetried, StepFailureEnd.Exhausted, StepFailureEnd.DurationLimit, StepFailureEnd.NoChange }.Select(end => end.Tag()));
        Assert.Throws<ArgumentOutOfRangeException>(() => StepFailureEnd.Retry.Tag());
    }
}
