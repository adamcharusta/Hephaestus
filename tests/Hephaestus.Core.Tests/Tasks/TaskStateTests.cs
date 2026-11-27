using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Tests.Tasks;

public class TaskStateTests
{
    [Fact]
    public void Every_state_has_the_kebab_case_name_of_its_label_and_reads_back()
    {
        Assert.Equal(
            ["queued", "running", "waiting-for-owner", "waiting-for-capacity", "pull-request-open", "failed", "cancelled", "withdrawn", "merged", "closed"],
            Enum.GetValues<TaskState>().Select(state => state.Tag()));
        foreach (var state in Enum.GetValues<TaskState>())
        {
            Assert.True(TaskStates.TryParse(state.Tag(), out var parsed));
            Assert.Equal(state, parsed);
        }
    }

    [Theory]
    [InlineData("Queued")]
    [InlineData("waitingForOwner")]
    [InlineData("")]
    [InlineData(null)]
    public void A_name_that_is_not_a_state_does_not_parse(string? tag)
    {
        Assert.False(TaskStates.TryParse(tag, out _));
    }

    [Fact]
    public void A_value_outside_the_enumeration_has_no_name()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((TaskState)99).Tag());
    }
}
