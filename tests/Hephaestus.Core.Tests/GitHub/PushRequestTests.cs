using Hephaestus.Core.GitHub;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.GitHub;

/// <summary>AD-4, AD-9, FR-44, AR-53, Story 3.7: no request exists for a ref that is not a Task branch, or for a bundle that has no tip to fast-forward.</summary>
public class PushRequestTests
{
    private static readonly string Tip = new('a', 40);
    private static readonly string Last = new('b', 40);
    private static readonly string Start = new('c', 40);
    private static readonly string Sha = new('d', 64);
    private static readonly Guid TaskId = Guid.Parse("018f4b6e-0000-7000-8000-000000000001");
    private static readonly Guid StepRunId = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");

    private static BundleReference Bundle(string? prerequisite) => new(BundleReference.CheckpointPath(TaskId, StepRunId), Sha, Tip, prerequisite);

    [Fact]
    public void A_request_names_the_Task_branch_the_bundle_and_the_start_commit_and_says_the_tips_before_and_after()
    {
        var request = new PushRequest(42, TaskId, StepRunId, "hephaestus/7-add-login", Bundle(Last), Start);

        Assert.Equal(Last, request.LastPushedTip);
        Assert.Equal(Tip, request.Tip);
        Assert.Equal(Start, request.StartCommit);
        Assert.Equal("hephaestus/7-add-login", request.Branch);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("develop")]
    [InlineData("hephaestus/")]
    [InlineData("hephaestus/x")]
    [InlineData("hephaestus/7-Add")]
    [InlineData("hephaestus/7-a/../../main")]
    [InlineData("refs/heads/hephaestus/7")]
    [InlineData("+hephaestus/7")]
    [InlineData("hephaestus/7:main")]
    [InlineData("")]
    public void A_ref_that_is_not_a_Task_branch_cannot_be_asked_for(string branch)
    {
        Assert.Throws<ArgumentException>(() => new PushRequest(42, TaskId, StepRunId, branch, Bundle(Last), Start));
        Assert.Throws<ArgumentException>(() => new BranchCheckRequest(42, branch, Last, Start));
    }

    [Fact]
    public void A_bundle_with_no_prerequisite_or_a_start_that_is_no_commit_cannot_be_pushed()
    {
        Assert.Throws<ArgumentException>(() => new PushRequest(42, TaskId, StepRunId, "hephaestus/7", Bundle(null), Start));
        Assert.Throws<ArgumentException>(() => new PushRequest(42, TaskId, StepRunId, "hephaestus/7", Bundle(Last), "HEAD"));
        Assert.Throws<ArgumentNullException>(() => new PushRequest(42, TaskId, StepRunId, "hephaestus/7", null!, Start));
        Assert.Throws<ArgumentException>(() => new BranchCheckRequest(42, "hephaestus/7", "main", Start));
        Assert.Throws<ArgumentException>(() => new BranchCheckRequest(42, "hephaestus/7", Last, "main"));
        Assert.Equal(("hephaestus/7", Last, Start), (new BranchCheckRequest(42, "hephaestus/7", Last, Start).Branch, new BranchCheckRequest(42, "hephaestus/7", Last, Start).LastPushedTip, new BranchCheckRequest(42, "hephaestus/7", Last, Start).StartCommit));
    }

    [Fact]
    public void Every_check_has_a_name_that_reads_back_and_a_value_outside_the_enumeration_has_none()
    {
        foreach (var check in Enum.GetValues<PushCheck>())
        {
            Assert.True(PushChecks.TryParse(check.Tag(), out var parsed));
            Assert.Equal(check, parsed);
            Assert.True(check.Tag().Length <= 16);
        }

        Assert.Equal(["import", "branch", "remote-moved", "fast-forward", "secret", "protected-path"], Enum.GetValues<PushCheck>().Select(PushChecks.Tag));
        Assert.False(PushChecks.TryParse("nonsense", out _));
        Assert.False(PushChecks.TryParse(null, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((PushCheck)99).Tag());
    }

    [Fact]
    public void The_results_say_what_happened()
    {
        Assert.Equal(PushOutcome.Pushed, PushResult.Pushed().Outcome);
        Assert.Equal(PushOutcome.AlreadyPushed, PushResult.AlreadyPushed().Outcome);
        var blocked = PushResult.Blocked(PushCheck.Secret, "a.txt");
        Assert.Equal((PushOutcome.Blocked, PushCheck.Secret, "a.txt"), (blocked.Outcome, blocked.Check, blocked.Detail));
        var failed = PushResult.Failed(MirrorFailure.Unreachable, "x");
        Assert.Equal((PushOutcome.Failed, MirrorFailure.Unreachable), (failed.Outcome, failed.Failure));
        Assert.Equal(BranchState.InSync, BranchCheckResult.InSync().State);
        Assert.Equal(("a1", BranchState.Moved), (BranchCheckResult.Moved("a1").Detail, BranchCheckResult.Moved("a1").State));
        Assert.Equal((BranchState.Failed, MirrorFailure.Rejected), (BranchCheckResult.Unavailable(MirrorFailure.Rejected).State, BranchCheckResult.Unavailable(MirrorFailure.Rejected).Failure));
    }
}
