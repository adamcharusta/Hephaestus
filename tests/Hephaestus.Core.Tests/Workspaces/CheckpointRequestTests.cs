using Hephaestus.Core.Aliases;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Workspaces;

/// <summary>Story 3.6, AD-9: a checkpoint is asked for a checkpointed kind of step, on a Task branch, from the whole bundle of the last pushed tip; and it comes to a bundle with the tip as prerequisite, or to nothing.</summary>
public class CheckpointRequestTests
{
    private const string Branch = "hephaestus/12-checkpoint";
    private static readonly string Tip = new('b', 40);
    private static readonly BundleReference Parent = new("task/step-checkout.bundle", new string('a', 64), Tip);
    private static CheckpointCommit CommitFor(Guid task, Guid step, StepKind kind = StepKind.Implementation) =>
        CheckpointCommit.Compose(new CommitIdentity("h[bot]", "h[bot]@users.noreply.github.com"), Language.English, kind, task, step, Agent.ClaudeCode, "m", Effort.Low);

    [Fact]
    public void A_request_holds_what_it_was_made_of()
    {
        var task = Guid.NewGuid();
        var step = Guid.NewGuid();

        var commit = CommitFor(task, step, StepKind.Correction);

        var request = new CheckpointRequest(task, step, StepKind.Correction, Branch, Parent, commit);

        Assert.Equal(task, request.TaskId);
        Assert.Equal(step, request.StepRunId);
        Assert.Equal(StepKind.Correction, request.Kind);
        Assert.Equal(Branch, request.Branch);
        Assert.Same(Parent, request.Parent);
        Assert.Same(commit, request.Commit);
    }

    [Theory]
    [InlineData(StepKind.Readiness)]
    [InlineData(StepKind.Planning)]
    [InlineData(StepKind.Review)]
    public void A_step_that_is_not_checkpointed_cannot_be_asked_for_a_checkpoint(StepKind kind)
    {
        var (task, step) = (Guid.NewGuid(), Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => new CheckpointRequest(task, step, kind, Branch, Parent, CommitFor(task, step)));
    }

    [Theory]
    [InlineData("main")]
    [InlineData("hephaestus/")]
    [InlineData("hephaestus/12 x")]
    [InlineData("")]
    public void A_branch_that_is_not_a_task_branch_is_refused(string branch)
    {
        var (task, step) = (Guid.NewGuid(), Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => new CheckpointRequest(task, step, StepKind.Implementation, branch, Parent, CommitFor(task, step)));
    }

    [Fact]
    public void The_parent_is_a_whole_bundle_and_nothing_is_missing()
    {
        var thin = new BundleReference("task/step-checkpoint.bundle", new string('a', 64), Tip, new string('c', 40));
        var (task, step) = (Guid.NewGuid(), Guid.NewGuid());
        var commit = CommitFor(task, step);

        Assert.Throws<ArgumentException>(() => new CheckpointRequest(task, step, StepKind.Implementation, Branch, thin, commit));
        Assert.Throws<ArgumentNullException>(() => new CheckpointRequest(task, step, StepKind.Implementation, Branch, null!, commit));
        Assert.Throws<ArgumentNullException>(() => new CheckpointRequest(task, step, StepKind.Implementation, Branch, Parent, null!));
    }

    [Fact]
    public void A_commit_composed_for_another_task_step_run_or_kind_is_refused_so_that_its_trailers_do_not_misattribute_the_checkpoint()
    {
        var (task, step) = (Guid.NewGuid(), Guid.NewGuid());
        var commit = CommitFor(task, step);

        Assert.Throws<ArgumentException>(() => new CheckpointRequest(Guid.NewGuid(), step, StepKind.Implementation, Branch, Parent, commit));
        Assert.Throws<ArgumentException>(() => new CheckpointRequest(task, Guid.NewGuid(), StepKind.Implementation, Branch, Parent, commit));
        Assert.Throws<ArgumentException>(() => new CheckpointRequest(task, step, StepKind.Correction, Branch, Parent, commit));
    }

    [Fact]
    public void An_unchanged_tree_is_a_result_with_no_bundle()
    {
        Assert.True(CheckpointResult.Unchanged.IsUnchanged);
        Assert.Null(CheckpointResult.Unchanged.Bundle);
    }

    [Fact]
    public void A_made_checkpoint_carries_a_bundle_with_the_last_pushed_tip_as_its_prerequisite()
    {
        var bundle = new BundleReference("task/step-checkpoint.bundle", new string('a', 64), new string('c', 40), Tip);

        var result = CheckpointResult.Made(bundle);

        Assert.False(result.IsUnchanged);
        Assert.Same(bundle, result.Bundle);
        Assert.Throws<ArgumentException>(() => CheckpointResult.Made(Parent));
        Assert.Throws<ArgumentNullException>(() => CheckpointResult.Made(null!));
    }
}
