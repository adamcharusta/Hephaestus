using Hephaestus.Core.GitHub;
using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Tests.GitHub;

/// <summary>Story 2.4, FR-11: the state label of a Task is <c>hephaestus:&lt;state&gt;</c>, the state in kebab-case, and an issue shows exactly one.</summary>
public class LabelsTests
{
    [Fact]
    public void Every_state_has_a_label_of_the_form_hephaestus_colon_state_in_kebab_case_and_it_reads_back()
    {
        Assert.Equal(
            [
                "hephaestus:queued", "hephaestus:running", "hephaestus:waiting-for-owner", "hephaestus:waiting-for-capacity", "hephaestus:pull-request-open",
                "hephaestus:failed", "hephaestus:cancelled", "hephaestus:withdrawn", "hephaestus:merged", "hephaestus:closed",
            ],
            Enum.GetValues<TaskState>().Select(Labels.State));
        foreach (var state in Enum.GetValues<TaskState>())
        {
            Assert.True(Labels.TryParseState(Labels.State(state), out var parsed));
            Assert.Equal(state, parsed);
        }
    }

    [Theory]
    [InlineData("Hephaestus:Queued", TaskState.Queued)]
    [InlineData("HEPHAESTUS:WAITING-FOR-OWNER", TaskState.WaitingForOwner)]
    public void GitHub_does_not_tell_label_names_apart_by_case_and_neither_does_the_reading(string label, TaskState expected)
    {
        Assert.True(Labels.TryParseState(label, out var state));
        Assert.Equal(expected, state);
    }

    [Theory]
    [InlineData("hephaestus")]
    [InlineData("hephaestus:")]
    [InlineData("hephaestus:bogus")]
    [InlineData("hephaestus:waitingForOwner")]
    [InlineData("primary:opus5.5")]
    [InlineData("queued")]
    [InlineData("")]
    [InlineData(null)]
    public void A_label_that_names_no_state_is_not_a_state_label(string? label)
    {
        Assert.False(Labels.TryParseState(label, out _));
    }

    [Fact]
    public void A_label_set_that_has_no_state_label_gets_the_state_of_the_Task_and_nothing_is_removed()
    {
        var (add, remove) = Labels.PlanState(["hephaestus", "bug"], TaskState.Queued);

        Assert.Equal(["hephaestus:queued"], add);
        Assert.Empty(remove);
    }

    [Fact]
    public void A_label_set_that_has_the_state_label_and_no_other_is_left_as_it_is()
    {
        var (add, remove) = Labels.PlanState(["hephaestus", "Hephaestus:Queued", "bug"], TaskState.Queued);

        Assert.Empty(add);
        Assert.Empty(remove);
    }

    [Fact]
    public void Another_state_label_is_replaced_and_the_labels_that_are_not_state_labels_are_never_touched()
    {
        var (add, remove) = Labels.PlanState(["hephaestus", "hephaestus:running", "hephaestus:failed", "hephaestus:bogus", "primary:opus5.5"], TaskState.Queued);

        Assert.Equal(["hephaestus:queued"], add);
        Assert.Equal(["hephaestus:running", "hephaestus:failed"], remove);
    }

    [Fact]
    public void A_label_set_with_the_right_label_and_a_wrong_one_loses_only_the_wrong_one()
    {
        var (add, remove) = Labels.PlanState(["hephaestus:queued", "hephaestus:merged"], TaskState.Queued);

        Assert.Empty(add);
        Assert.Equal(["hephaestus:merged"], remove);
    }

    [Theory]
    [InlineData(TaskState.Queued, true)]
    [InlineData(TaskState.Running, true)]
    [InlineData(TaskState.WaitingForCapacity, true)]
    [InlineData(TaskState.PullRequestOpen, true)]
    [InlineData(TaskState.WaitingForOwner, false)]
    [InlineData(TaskState.Failed, false)]
    [InlineData(TaskState.Cancelled, false)]
    [InlineData(TaskState.Withdrawn, false)]
    [InlineData(TaskState.Merged, false)]
    [InlineData(TaskState.Closed, false)]
    public void Only_a_Task_that_is_queued_or_in_progress_moves_to_waiting_for_the_Owner_when_an_effect_cannot_be_made(TaskState state, bool moves)
    {
        Assert.Equal(moves, state.MovesToWaitingForOwnerWhenItCannotGoOn());
    }

    [Theory]
    [InlineData("hephaestus", "hephaestus")]
    [InlineData("Hephaestus", "hephaestus")]
    [InlineData("HEPHAESTUS", "hephaestus")]
    [InlineData("hephaestus:queued", null)]
    [InlineData("bug", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_the_Marker_Label_is_one_that_Hephaestus_reads_for_now_and_it_is_named_as_it_is_written_here(string? label, string? expected)
    {
        Assert.Equal(expected, Labels.ReadLabel(label));
    }
}
