using Hephaestus.Core.Aliases;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Workspaces;

/// <summary>Story 3.6, AD-9, AR-33, AR-75: Core supplies the whole message and identity of a checkpoint commit: a subject in the Task's language and the five trailers, which are never translated.</summary>
public class CheckpointCommitTests
{
    private static readonly Guid TaskId = Guid.Parse("018f4b6e-0000-7000-8000-000000000001");
    private static readonly Guid StepRunId = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");
    private static readonly CommitIdentity Identity = new("hephaestus[bot]", "hephaestus[bot]@users.noreply.github.com");

    private static CheckpointCommit Compose(Language language = Language.English, StepKind kind = StepKind.Implementation, string model = "opus5.5") =>
        CheckpointCommit.Compose(Identity, language, kind, TaskId, StepRunId, Agent.CodexCli, model, Effort.ExtraHigh);

    [Fact]
    public void The_message_is_a_subject_a_blank_line_and_the_five_trailers_in_a_fixed_order()
    {
        var commit = Compose();

        Assert.Equal(
            "Checkpoint of the work of the implementation step\n"
            + "\n"
            + "Hephaestus-Task: 018f4b6e-0000-7000-8000-000000000001\n"
            + "Hephaestus-Step: 018f4b6e-0000-7000-8000-000000000002\n"
            + "Hephaestus-Agent: codex-cli\n"
            + "Hephaestus-Model: opus5.5\n"
            + "Hephaestus-Effort: xhigh\n",
            commit.Message);
        Assert.Same(Identity, commit.Identity);
        Assert.Equal((TaskId, StepRunId, StepKind.Implementation), (commit.TaskId, commit.StepRunId, commit.Kind));
    }

    [Fact]
    public void A_step_with_no_Effort_asked_for_says_so_in_the_trailer_and_names_none()
    {
        var commit = CheckpointCommit.Compose(Identity, Language.English, StepKind.Implementation, TaskId, StepRunId, Agent.ClaudeCode, "opus5.5", effort: null);

        Assert.Contains("Hephaestus-Effort: default\n", commit.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_subject_is_in_the_language_of_the_task_and_the_trailers_are_not_translated()
    {
        var polish = Compose(Language.Polish, StepKind.Correction);

        Assert.StartsWith("Checkpoint pracy kroku correction\n", polish.Message, StringComparison.Ordinal);
        Assert.Contains("\nHephaestus-Task: ", polish.Message, StringComparison.Ordinal);
        Assert.Contains("\nHephaestus-Effort: xhigh\n", polish.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(StepKind.Readiness)]
    [InlineData(StepKind.Planning)]
    [InlineData(StepKind.Review)]
    public void A_step_that_is_not_checkpointed_has_no_commit(StepKind kind)
    {
        Assert.Throws<ArgumentException>(() => Compose(kind: kind));
    }

    [Theory]
    [InlineData("")]
    [InlineData("two lines\nHephaestus-Task: forged")]
    [InlineData("has space")]
    [InlineData("-leading-dash")]
    public void A_model_that_could_forge_a_trailer_or_is_not_one_is_refused(string model)
    {
        Assert.Throws<ArgumentException>(() => Compose(model: model));
    }

    [Fact]
    public void A_commit_needs_an_identity()
    {
        Assert.Throws<ArgumentNullException>(() => CheckpointCommit.Compose(null!, Language.English, StepKind.Implementation, TaskId, StepRunId, Agent.ClaudeCode, "m", Effort.Low));
    }

    [Fact]
    public void Every_agent_and_effort_has_a_tag_a_trailer_can_carry()
    {
        foreach (var agent in Enum.GetValues<Agent>())
        {
            foreach (var effort in Enum.GetValues<Effort>())
            {
                var message = CheckpointCommit.Compose(Identity, Language.English, StepKind.Rework, TaskId, StepRunId, agent, "m", effort).Message;

                Assert.Contains($"Hephaestus-Agent: {agent.Tag()}\n", message, StringComparison.Ordinal);
                Assert.Contains($"Hephaestus-Effort: {effort.Tag()}\n", message, StringComparison.Ordinal);
            }
        }
    }
}
