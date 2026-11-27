using Hephaestus.Core.Escalations;
using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Tests.Tasks;

/// <summary>Story 2.10: what the loop reads of a comment's text, and the row of the lifecycle table an answer takes.</summary>
public class CommentContentTests
{
    [Fact]
    public void The_hash_is_the_lower_case_SHA_256_of_the_text_as_given()
    {
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", CommentContent.Hash(string.Empty));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", CommentContent.Hash("abc"));
        Assert.NotEqual(CommentContent.Hash("abc"), CommentContent.Hash("abc\n"));
    }

    [Theory]
    [InlineData("/hephaestus confirm", true)]
    [InlineData("/hephaestus", true)]
    [InlineData("/hephaestus confirm\r\nmore", true)]
    [InlineData("   /hephaestus confirm", true)]
    [InlineData("\n/hephaestus confirm", true)]
    [InlineData("/HEPHAESTUS confirm", true)]
    [InlineData("/hephaestusfoo", true)]
    [InlineData("\uFEFF/hephaestus confirm", true)]
    [InlineData("\u200B /hephaestus confirm", true)]
    [InlineData("Use Postgres.\n/hephaestus confirm", false)]
    [InlineData("please /hephaestus confirm", false)]
    [InlineData("hephaestus confirm", false)]
    [InlineData("", false)]
    public void A_comment_is_a_command_when_its_first_line_begins_with_the_prefix(string body, bool command)
    {
        Assert.Equal(command, CommentContent.IsCommand(body));
    }

    [Theory]
    [InlineData(TaskState.WaitingForOwner, TaskState.Queued)]
    [InlineData(TaskState.Queued, null)]
    [InlineData(TaskState.Running, null)]
    [InlineData(TaskState.Closed, null)]
    public void An_answer_takes_only_a_Task_that_waits_for_the_Owner_back_to_the_Queue(TaskState from, TaskState? to)
    {
        Assert.Equal(to, TaskLifecycle.AfterAnswer(from));
    }

    [Fact]
    public void An_Escalation_that_was_answered_has_its_own_name()
    {
        Assert.Equal("answered", EscalationOutcome.Answered.Tag());
        Assert.True(EscalationOutcomes.TryParse("answered", out var outcome));
        Assert.Equal(EscalationOutcome.Answered, outcome);
    }
}
