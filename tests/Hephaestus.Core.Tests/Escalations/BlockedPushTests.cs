using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Tests.Escalations;

/// <summary>AD-9, AD-19, FR-48, Story 3.7: work that was not pushed is escalated through the one path, the comment names the check and what it found, and a branch that moved waits for `confirm`, not for a reply.</summary>
public class BlockedPushTests
{
    private static readonly MovingTime Time = new();

    private static TaskRecord Task(TaskState state = TaskState.Running) => new(Guid.CreateVersion7(), 101, 7, state, "hephaestus/7-add-a-login-page", Time.Now, Time.Now);

    private static Escalation Blocked(PushCheck check, string detail, string resume = ResumeConditions.TrustedReply) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), EscalationKind.PushBlocked, detail, [], resume, "s018f4b6e00007000800000000000000", Time.Now, Check: check);

    [Fact]
    public void A_blocked_push_makes_the_plan_of_the_one_path_with_the_check_and_what_it_found()
    {
        var task = Task();

        var plan = EscalationPath.ForBlockedPush(task, PushCheck.ProtectedPath, ".github/workflows/ci.yml,\n  hephaestus.yml", "s1", Time)!;

        Assert.Equal((TaskState.Running, TaskState.WaitingForOwner), (plan.From, plan.To));
        var escalation = plan.Escalation;
        Assert.Equal((EscalationKind.PushBlocked, PushCheck.ProtectedPath, ResumeConditions.TrustedReply, "s1"), (escalation.Kind, escalation.Check, escalation.ResumeCondition, escalation.Cause));
        Assert.Equal(".github/workflows/ci.yml, hephaestus.yml", escalation.Decision);
        Assert.Empty(escalation.Options);
        Assert.Null(escalation.Effect);
        Assert.Equal([EffectKind.Label, EffectKind.Comment, EffectKind.Notification], plan.Effects.Select(e => e.Kind));
    }

    [Fact]
    public void A_branch_that_moved_waits_for_confirm_and_nothing_else_does()
    {
        foreach (var check in Enum.GetValues<PushCheck>())
        {
            var plan = EscalationPath.ForBlockedPush(Task(), check, null, "s1", Time)!;

            Assert.Equal(check == PushCheck.RemoteMoved ? ResumeConditions.Confirm : ResumeConditions.TrustedReply, plan.Escalation.ResumeCondition);
            Assert.Equal(string.Empty, plan.Escalation.Decision);
        }
    }

    [Theory]
    [InlineData(TaskState.Queued, true)]
    [InlineData(TaskState.WaitingForCapacity, true)]
    [InlineData(TaskState.PullRequestOpen, true)]
    [InlineData(TaskState.WaitingForOwner, false)]
    [InlineData(TaskState.Cancelled, false)]
    public void Only_a_Task_in_a_state_that_moves_is_escalated(TaskState state, bool escalated)
    {
        Assert.Equal(escalated, EscalationPath.ForBlockedPush(Task(state), PushCheck.Secret, null, "s1", Time) is not null);
    }

    [Fact]
    public void The_kind_has_a_name_that_reads_back()
    {
        Assert.Equal("push-blocked", EscalationKind.PushBlocked.Tag());
        Assert.True(EscalationKinds.TryParse("push-blocked", out var kind));
        Assert.Equal(EscalationKind.PushBlocked, kind);
    }

    [Theory]
    [InlineData(PushCheck.Import, "the bundle of the work could not be imported.")]
    [InlineData(PushCheck.Branch, "the bundle does not carry the Task branch alone.")]
    [InlineData(PushCheck.FastForward, "the work is not a fast-forward of the last pushed commit.")]
    [InlineData(PushCheck.Secret, "the work holds the value of a stored secret.")]
    [InlineData(PushCheck.ProtectedPath, "the work changes a protected path.")]
    public void The_comment_names_the_check_says_what_it_means_and_lists_what_it_found_and_how_to_go_on(PushCheck check, string meaning)
    {
        var text = EscalationComment.Compose(Blocked(check, "src/a.txt, src/b.txt"), Language.English);

        Assert.Equal(
            $"""
            **Hephaestus needs a decision.**

            Hephaestus did not push the work of this Task, because the `{check.Tag()}` check failed: {meaning} Found: src/a.txt, src/b.txt
            A. Reply here and say what to change: the Task returns to the Queue with your reply as the answer.
            B. Remove the `hephaestus` label to cancel the Task.

            Reply on this issue. Any reply from a Trusted User returns the Task to the Queue with the answer.
            """.ReplaceLineEndings("\n"),
            text);
    }

    [Fact]
    public void A_check_that_found_nothing_to_list_says_no_more_than_it_failed()
    {
        var text = EscalationComment.Compose(Blocked(PushCheck.FastForward, string.Empty), Language.English);

        Assert.Contains("check failed: the work is not a fast-forward of the last pushed commit.\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Found:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_branch_that_moved_names_the_commits_and_says_a_reply_does_not_end_the_wait()
    {
        var text = EscalationComment.Compose(Blocked(PushCheck.RemoteMoved, "0123456789ab, ba9876543210", ResumeConditions.Confirm), Language.English);

        Assert.Equal(
            """
            **Hephaestus needs a decision.**

            The Task branch on GitHub is not at the commit Hephaestus last pushed, so no step runs on it and nothing is pushed. Commits that Hephaestus did not push: 0123456789ab, ba9876543210
            A. A Trusted User adopts those commits with the `confirm` command, and the Task goes on from them.
            B. Remove the `hephaestus` label to cancel the Task.

            A reply does not end this wait. A Trusted User ends it with the `confirm` command, or cancels the Task.
            """.ReplaceLineEndings("\n"),
            text);
    }

    [Fact]
    public void A_branch_that_is_gone_has_no_commits_to_name()
    {
        var text = EscalationComment.Compose(Blocked(PushCheck.RemoteMoved, string.Empty, ResumeConditions.Confirm), Language.English);

        Assert.Contains("and nothing is pushed.\nA. ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Commits that", text, StringComparison.Ordinal);
    }

    [Fact]
    public void What_the_check_found_is_neutralised_because_a_path_is_what_an_Agent_named()
    {
        var text = EscalationComment.Compose(Blocked(PushCheck.Secret, "<!-- x --> @alice/Fixes #4.txt"), Language.English);

        Assert.DoesNotContain("<!--", text, StringComparison.Ordinal);
        Assert.DoesNotContain("@alice", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_effect_that_pushes_and_could_not_be_completed_says_the_work_could_not_be_pushed()
    {
        var failed = new Escalation(Guid.CreateVersion7(), Guid.CreateVersion7(), EscalationKind.EffectFailed, string.Empty, [], ResumeConditions.TrustedReply, "s1", Time.Now, new FailedEffect("k", EffectKind.Push, "push|x", EffectReasons.Unreachable));

        Assert.Contains("The work of this Task could not be pushed to GitHub, so this Task cannot go on: GitHub stayed unreachable until the limit of attempts.", EscalationComment.Compose(failed, Language.English), StringComparison.Ordinal);
        Assert.Contains("Nie udało się wypchnąć pracy tego Taska na GitHub", EscalationComment.Compose(failed, Language.Polish), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_sentence_exists_in_Polish_too()
    {
        foreach (var check in Enum.GetValues<PushCheck>())
        {
            var resume = check == PushCheck.RemoteMoved ? ResumeConditions.Confirm : ResumeConditions.TrustedReply;
            var text = EscalationComment.Compose(Blocked(check, "x", resume), Language.Polish);

            Assert.DoesNotContain("comment.escalation", text, StringComparison.Ordinal);
            Assert.NotEqual(EscalationComment.Compose(Blocked(check, "x", resume), Language.English), text);
        }
    }
}
