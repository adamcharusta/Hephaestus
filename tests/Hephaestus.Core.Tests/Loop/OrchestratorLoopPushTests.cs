using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Loop;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>
/// Story 3.7, AD-5, AD-9, AD-19, FR-20, FR-30: a push is a journaled effect that the loop makes through the gateway, resolved by reading after a crash and never made twice; a check that fails ends it as refused and raises an Escalation
/// that names the check, which keeps the Workspace; a branch that moved holds the Task until `confirm`; and the loop deletes the bundle's file once the result is journaled.
/// </summary>
public class OrchestratorLoopPushTests
{
    private const int Issue = 7;
    private static readonly Guid StepRunId = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");
    private static readonly string Tip = new('a', 40);
    private static readonly string Last = new('b', 40);
    private static readonly string Start = new('c', 40);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly LoopStores _stores = new();
    private readonly MovingTime _time = new();
    private readonly FakeBranches _branches;

    public OrchestratorLoopPushTests()
    {
        _stores.TimeProvider = _time;
        _stores.AppSlug = "hephaestus-test";
        _branches = new FakeBranches(_stores);
    }

    private OrchestratorLoop NewLoop(bool withBranches = true) =>
        new(_stores, _stores, _stores, _stores.Github, _stores.Notifier, _time, new LoopHeartbeat(_time), branches: withBranches ? _branches : null);

    private PushIntent Intent => PushIntent.For(StepRunId, new BundleReference(BundleReference.CheckpointPath(_stores.Tasks[0].Id, StepRunId), new string('d', 64), Tip, Last), Start);

    /// <summary>A Task that is in <paramref name="state"/> with the intent of a push journaled, as the end of a step run journals it.</summary>
    private async Task<OrchestratorLoop> WithPushIntentAsync(TaskState state = TaskState.Running, string? detail = null)
    {
        var loop = NewLoop();
        await _stores.AppendAsync([LoopStores.Labeled("issue-event:1")], Token);
        await loop.TickAsync(Token);
        _stores.Tasks[0] = _stores.Tasks[0] with { State = state };
        var task = _stores.Tasks[0];
        var effect = Intent.ToNewEffect(task.Id) with { Detail = detail ?? Intent.Detail };
        var ordinal = 0;
        _stores.Journal.Add(new JournalEntry(
            _stores.Journal.Count + 1,
            JournalEntryKind.Intent,
            new Effect(EffectKeys.For(task.Id, effect.Cause, EffectKind.Push, ordinal), task.Id, effect.Cause, EffectKind.Push, effect.Detail, ordinal),
            null,
            null,
            0,
            null,
            _time.Now));
        return loop;
    }

    private JournalEntry PushResult() => _stores.Journal.Single(e => e.Effect.Kind == EffectKind.Push && e.Entry == JournalEntryKind.Result);

    [Fact]
    public async Task A_push_is_made_with_the_key_the_journal_holds_and_the_request_the_intent_names_and_ends_as_applied()
    {
        var loop = await WithPushIntentAsync();

        var report = await loop.TickAsync(Token);

        var (request, key) = Assert.Single(_branches.Pushes);
        var task = _stores.Tasks[0];
        Assert.Equal($"{task.Id}:{EffectKeys.StepCause(StepRunId)}:push:0", key);
        Assert.Equal((task.RepositoryId, task.Id, StepRunId, task.Branch), (request.RepositoryId, request.TaskId, request.StepRunId, request.Branch));
        Assert.Equal((Last, Tip, Start), (request.LastPushedTip, request.Tip, request.StartCommit));
        Assert.Equal(BundleReference.CheckpointPath(task.Id, StepRunId), request.Bundle.Path);
        Assert.Equal((EffectResult.Applied, null), (PushResult().Result, PushResult().Reason));
        Assert.Null(report.Failure);
        Assert.Empty(report.Problems ?? []);
    }

    [Fact]
    public async Task The_bundle_is_deleted_once_and_only_after_the_result_is_journaled()
    {
        var loop = await WithPushIntentAsync();

        await loop.TickAsync(Token);

        var (bundle, _, hadResult) = Assert.Single(_branches.Deleted);
        Assert.Equal(Intent.Bundle(_stores.Tasks[0].Id), bundle);
        Assert.True(hadResult);
    }

    [Fact]
    public async Task A_push_that_was_made_and_not_recorded_is_found_on_the_branch_and_recorded_as_already_in_place()
    {
        _branches.Answers.Enqueue(Hephaestus.Core.GitHub.PushResult.AlreadyPushed());
        var loop = await WithPushIntentAsync();

        await loop.TickAsync(Token);

        Assert.Equal(EffectResult.AlreadyInPlace, PushResult().Result);
        Assert.Single(_branches.Deleted);
        Assert.Equal(TaskState.Running, _stores.Tasks[0].State);
    }

    [Fact]
    public async Task An_effect_with_a_result_is_not_made_again_by_a_later_tick_or_a_restart()
    {
        var loop = await WithPushIntentAsync();
        await loop.TickAsync(Token);

        await loop.TickAsync(Token);
        await NewLoop().TickAsync(Token);

        Assert.Single(_branches.Pushes);
        Assert.Single(_branches.Deleted);
    }

    [Theory]
    [InlineData(PushCheck.Import, "import")]
    [InlineData(PushCheck.Branch, "branch")]
    [InlineData(PushCheck.FastForward, "fast-forward")]
    [InlineData(PushCheck.Secret, "secret")]
    [InlineData(PushCheck.ProtectedPath, "protected-path")]
    public async Task A_check_that_fails_ends_the_effect_as_refused_with_the_check_and_raises_an_Escalation_that_names_it(PushCheck check, string tag)
    {
        _branches.Answers.Enqueue(Hephaestus.Core.GitHub.PushResult.Blocked(check, "src/a.txt"));
        var loop = await WithPushIntentAsync();

        var report = await loop.TickAsync(Token);

        Assert.Equal((EffectResult.Refused, tag), (PushResult().Result, PushResult().Reason));
        var task = _stores.Tasks[0];
        Assert.Equal(TaskState.WaitingForOwner, task.State);
        var escalation = Assert.Single(_stores.Escalations);
        Assert.Equal((EscalationKind.PushBlocked, check, "src/a.txt", ResumeConditions.TrustedReply), (escalation.Kind, escalation.Check, escalation.Decision, escalation.ResumeCondition));
        Assert.Equal(EffectKeys.StepCause(StepRunId), escalation.Cause);
        var problem = Assert.Single(report.Problems!);
        Assert.Equal((task.Id, EffectResult.Refused, tag, true), (problem.TaskId, problem.Result, problem.Reason, problem.MovedToWaitingForOwner));
        await loop.TickAsync(Token);
        var comment = Assert.Single(_stores.Github.Comments).Text;
        Assert.Contains($"`{tag}`", comment, StringComparison.Ordinal);
        Assert.Contains("src/a.txt", comment, StringComparison.Ordinal);
        Assert.Single(_stores.Notifier.Calls);
        Assert.Single(_branches.Deleted);
    }

    [Fact]
    public async Task A_blocked_push_makes_no_effect_that_removes_anything_so_the_Workspace_is_kept()
    {
        _branches.Answers.Enqueue(Hephaestus.Core.GitHub.PushResult.Blocked(PushCheck.Secret));
        var loop = await WithPushIntentAsync();

        await loop.TickAsync(Token);
        await loop.TickAsync(Token);

        // The effects there are, and the ones that end a Workspace are none of them: a volume is removed only by the cleanup of Story 3.20, which waits for the journal to show the tip pushed.
        Assert.All(_stores.Journal, entry => Assert.Contains(entry.Effect.Kind, new[] { EffectKind.Label, EffectKind.Comment, EffectKind.Notification, EffectKind.Push }));
        Assert.Equal(EffectResult.Refused, PushResult().Result);
        Assert.Equal(TaskState.WaitingForOwner, _stores.Tasks[0].State);
    }

    [Fact]
    public async Task A_branch_that_moved_holds_the_Task_until_confirm_and_a_reply_does_not_end_the_wait()
    {
        _branches.Answers.Enqueue(Hephaestus.Core.GitHub.PushResult.Blocked(PushCheck.RemoteMoved, "0123456789ab, ba9876543210"));
        var loop = await WithPushIntentAsync();
        await loop.TickAsync(Token);

        var escalation = Assert.Single(_stores.Escalations);
        Assert.Equal((PushCheck.RemoteMoved, ResumeConditions.Confirm), (escalation.Check, escalation.ResumeCondition));
        await loop.TickAsync(Token);
        var comment = Assert.Single(_stores.Github.Comments).Text;
        Assert.Contains("0123456789ab, ba9876543210", comment, StringComparison.Ordinal);
        Assert.Contains("`confirm`", comment, StringComparison.Ordinal);

        await _stores.AppendAsync([LoopStores.Comment("comment-event:1", at: _time.Now.AddMinutes(1))], Token);
        await loop.TickAsync(Token);

        Assert.Equal(TaskState.WaitingForOwner, _stores.Tasks[0].State);
        Assert.Null(Assert.Single(_stores.Escalations).ClosedAt);
    }

    [Fact]
    public async Task A_Task_that_waits_already_because_the_step_asked_a_question_still_has_its_work_pushed_and_asks_nothing_more()
    {
        var loop = await WithPushIntentAsync(TaskState.WaitingForOwner);

        await loop.TickAsync(Token);

        Assert.Equal(EffectResult.Applied, PushResult().Result);
        Assert.Empty(_stores.Escalations);
        Assert.Single(_branches.Pushes);
    }

    [Fact]
    public async Task A_check_that_fails_for_a_Task_that_waits_already_is_refused_and_raises_no_second_Escalation()
    {
        _branches.Answers.Enqueue(Hephaestus.Core.GitHub.PushResult.Blocked(PushCheck.Secret));
        var loop = await WithPushIntentAsync(TaskState.WaitingForOwner);

        await loop.TickAsync(Token);

        Assert.Equal((EffectResult.Refused, "secret"), (PushResult().Result, PushResult().Reason));
        Assert.Empty(_stores.Escalations);
        Assert.Single(_branches.Deleted);
    }

    [Theory]
    [InlineData(TaskState.Cancelled)]
    [InlineData(TaskState.Closed)]
    [InlineData(TaskState.Failed)]
    [InlineData(TaskState.Withdrawn)]
    [InlineData(TaskState.Merged)]
    public async Task A_Task_that_has_ended_is_pushed_nothing_and_its_bundle_is_deleted(TaskState state)
    {
        var loop = await WithPushIntentAsync(state);

        await loop.TickAsync(Token);

        Assert.Equal(EffectResult.Superseded, PushResult().Result);
        Assert.Empty(_branches.Pushes);
        Assert.Single(_branches.Deleted);
    }

    [Fact]
    public async Task A_push_that_cannot_be_made_for_a_reason_that_is_not_the_works_is_retried_and_the_bundle_is_kept_for_the_retry()
    {
        _branches.Answers.Enqueue(Hephaestus.Core.GitHub.PushResult.Failed(MirrorFailure.Unreachable));
        var loop = await WithPushIntentAsync();

        var report = await loop.TickAsync(Token);

        Assert.Equal(1, report.EffectsRetried);
        var retry = Assert.Single(_stores.Journal, e => e.Entry == JournalEntryKind.Retry);
        Assert.Equal((1, EffectReasons.Unreachable), (retry.Failures, retry.Reason));
        Assert.Equal(_time.Now + EffectRetries.DelayAfter(1), retry.NextAttemptAt);
        Assert.Empty(_branches.Deleted);
        Assert.Equal(TaskState.Running, _stores.Tasks[0].State);

        _time.Advance(TimeSpan.FromMinutes(5));
        await loop.TickAsync(Token);

        Assert.Equal(2, _branches.Pushes.Count);
        Assert.Equal(EffectResult.Applied, PushResult().Result);
        Assert.Single(_branches.Deleted);
    }

    [Fact]
    public async Task A_push_that_stays_unreachable_gives_up_at_the_bound_and_the_Task_waits_with_a_comment_that_says_the_work_could_not_be_pushed()
    {
        _stores.Settings["effectRetries"] = "0";
        _branches.Answers.Enqueue(Hephaestus.Core.GitHub.PushResult.Failed(MirrorFailure.GitFailed));
        var loop = await WithPushIntentAsync();

        var report = await loop.TickAsync(Token);

        Assert.Equal((EffectResult.GaveUp, EffectReasons.Unreachable), (PushResult().Result, PushResult().Reason));
        var escalation = Assert.Single(_stores.Escalations);
        Assert.Equal((EscalationKind.EffectFailed, EffectKind.Push), (escalation.Kind, escalation.Effect!.Kind));
        Assert.Equal(TaskState.WaitingForOwner, _stores.Tasks[0].State);
        Assert.Equal(EffectResult.GaveUp, Assert.Single(report.Problems!).Result);
        await loop.TickAsync(Token);
        Assert.Contains("could not be pushed to GitHub", Assert.Single(_stores.Github.Comments).Text, StringComparison.Ordinal);
        Assert.Single(_branches.Deleted);
    }

    [Theory]
    [InlineData(MirrorFailure.NotConnected, EffectReasons.NotConnected, EffectResult.GaveUp)]
    [InlineData(MirrorFailure.KeyUnreadable, EffectReasons.KeyUnreadable, EffectResult.GaveUp)]
    [InlineData(MirrorFailure.NotInstalled, EffectReasons.AppNotInstalled, EffectResult.Refused)]
    [InlineData(MirrorFailure.Rejected, EffectReasons.Rejected, EffectResult.Refused)]
    [InlineData(MirrorFailure.UnknownProject, EffectReasons.Rejected, EffectResult.Refused)]
    [InlineData(MirrorFailure.CommitMissing, EffectReasons.Rejected, EffectResult.Refused)]
    public async Task Each_reason_a_push_cannot_be_made_is_treated_as_the_other_effects_on_GitHub_are(MirrorFailure failure, string reason, EffectResult result)
    {
        _stores.Settings["effectRetries"] = "0";
        _branches.Answers.Enqueue(Hephaestus.Core.GitHub.PushResult.Failed(failure));
        var loop = await WithPushIntentAsync();

        await loop.TickAsync(Token);

        Assert.Equal((result, reason), (PushResult().Result, PushResult().Reason));
    }

    [Fact]
    public async Task A_gateway_that_throws_is_treated_as_one_that_did_not_answer()
    {
        _branches.Throws = new InvalidOperationException("A gateway built without the masker pushes nothing.");
        var loop = await WithPushIntentAsync();

        var report = await loop.TickAsync(Token);

        Assert.Equal(1, report.EffectsRetried);
        Assert.IsType<InvalidOperationException>(report.Failure);
        Assert.Empty(_branches.Deleted);
    }

    [Fact]
    public async Task A_host_with_no_gateway_for_branches_refuses_the_intent_and_pushes_nothing()
    {
        await WithPushIntentAsync();

        var report = await NewLoop(withBranches: false).TickAsync(Token);

        Assert.Equal((EffectResult.Refused, EffectReasons.UnknownEffect), (PushResult().Result, PushResult().Reason));
        Assert.Empty(_branches.Pushes);
        Assert.Equal(EffectResult.Refused, Assert.Single(report.Problems!).Result);
    }

    [Theory]
    [InlineData("push|nothing")]
    [InlineData("label|queued")]
    public async Task An_intent_this_version_cannot_read_is_refused_and_pushes_nothing(string detail)
    {
        var loop = await WithPushIntentAsync(detail: detail);

        await loop.TickAsync(Token);

        Assert.Equal((EffectResult.Refused, EffectReasons.UnknownEffect), (PushResult().Result, PushResult().Reason));
        Assert.Empty(_branches.Pushes);
    }

    [Fact]
    public async Task A_Task_whose_branch_is_no_Task_branch_is_refused_before_anything_is_asked_of_GitHub_and_the_bundle_is_deleted()
    {
        var loop = await WithPushIntentAsync();
        _stores.Tasks[0] = _stores.Tasks[0] with { Branch = "main" };

        await loop.TickAsync(Token);

        Assert.Equal((EffectResult.Refused, EffectReasons.UnknownEffect), (PushResult().Result, PushResult().Reason));
        Assert.Empty(_branches.Pushes);
        Assert.Single(_branches.Deleted);
    }

    [Fact]
    public async Task The_sentences_of_a_blocked_push_are_in_the_language_of_the_installation()
    {
        _stores.Settings["language"] = "pl";
        _branches.Answers.Enqueue(Hephaestus.Core.GitHub.PushResult.Blocked(PushCheck.ProtectedPath, ".github/workflows/ci.yml"));
        var loop = await WithPushIntentAsync();

        await loop.TickAsync(Token);

        await loop.TickAsync(Token);
        var comment = Assert.Single(_stores.Github.Comments).Text;
        Assert.Contains("chronioną ścieżkę", comment, StringComparison.Ordinal);
        Assert.Contains("`protected-path`", comment, StringComparison.Ordinal);
    }
}
