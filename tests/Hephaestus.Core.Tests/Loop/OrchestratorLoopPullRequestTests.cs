using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Loop;
using Hephaestus.Core.PullRequests;
using Hephaestus.Core.Tasks;
using static Hephaestus.Core.Tests.PullRequests.PullRequestFixtures;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>
/// Story 3.13, AD-5, FR-21, FR-36: the publication of a Task's pull request is a journaled effect that the loop makes through the gateway, from the Task branch to the Base Branch; its result moves the Task to <c>pull-request-open</c> with the
/// intent of its label; one made and not recorded is found by the head branch and never opened twice; a Task published again has the same pull request updated; and it is no step run, so it holds no slot.
/// </summary>
public class OrchestratorLoopPullRequestTests
{
    private const int Issue = 7;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly LoopStores _stores = new();
    private readonly MovingTime _time = new();
    private readonly FakePullRequests _pulls = new();

    public OrchestratorLoopPullRequestTests()
    {
        _stores.TimeProvider = _time;
        _stores.AppSlug = "hephaestus-test";
    }

    private OrchestratorLoop NewLoop(bool withPullRequests = true) =>
        new(_stores, _stores, _stores, _stores.Github, _stores.Notifier, _time, new LoopHeartbeat(_time), pullRequests: withPullRequests ? _pulls : null);

    /// <summary>A Task that is in <paramref name="state"/> with the intent of a publication journaled, as the end of Validation journals it.</summary>
    private async Task<OrchestratorLoop> WithIntentAsync(TaskState state = TaskState.Running, string? detail = null, string? payload = null, Guid? stepRun = null)
    {
        var loop = NewLoop();
        await _stores.AppendAsync([LoopStores.Labeled("issue-event:1")], Token);
        await loop.TickAsync(Token);
        _stores.Tasks[0] = _stores.Tasks[0] with { State = state };
        await JournalAsync(loop, detail, payload, stepRun);
        return loop;
    }

    private Task JournalAsync(OrchestratorLoop _, string? detail = null, string? payload = null, Guid? stepRun = null)
    {
        var task = _stores.Tasks[0];
        var intent = new PullRequestIntent(stepRun ?? StepRunId, Tip);
        var effect = intent.ToNewEffect(task.Id, Facts()) with { Detail = detail ?? intent.Detail, Payload = payload ?? intent.ToNewEffect(task.Id, Facts()).Payload };
        var ordinal = _stores.Journal.Count(e => e.Entry == JournalEntryKind.Intent && e.Effect.Kind == EffectKind.PullRequest && e.Effect.Cause == effect.Cause);
        _stores.Journal.Add(new JournalEntry(
            _stores.Journal.Count + 1,
            JournalEntryKind.Intent,
            new Effect(EffectKeys.For(task.Id, effect.Cause, EffectKind.PullRequest, ordinal), task.Id, effect.Cause, EffectKind.PullRequest, effect.Detail, ordinal, Payload: effect.Payload),
            null,
            null,
            0,
            null,
            _time.Now));
        return Task.CompletedTask;
    }

    private JournalEntry Result() => _stores.Journal.Single(e => e.Effect.Kind == EffectKind.PullRequest && e.Entry == JournalEntryKind.Result);

    [Fact]
    public async Task A_pull_request_is_opened_from_the_Task_branch_to_the_Base_Branch_with_the_key_the_journal_holds_and_ends_as_applied()
    {
        var loop = await WithIntentAsync();

        var report = await loop.TickAsync(Token);

        var task = _stores.Tasks[0];
        var (request, key) = Assert.Single(_pulls.Publications);
        Assert.Equal($"{task.Id}:{EffectKeys.StepCause(StepRunId)}:pull-request:0", key);
        Assert.Equal((task.RepositoryId, task.Branch, "main", "Add a login page"), (request.RepositoryId, request.Head, request.Base, request.Title));
        Assert.Equal((EffectResult.Applied, null), (Result().Result, Result().Reason));
        Assert.Null(report.Failure);
        Assert.Empty(report.Problems ?? []);
        Assert.Equal(1, _pulls.OpenOf(task.Branch));
    }

    [Fact]
    public async Task The_description_is_composed_by_Core_from_the_facts_of_the_intent_and_names_the_issue_of_the_Task()
    {
        var loop = await WithIntentAsync();

        await loop.TickAsync(Token);

        var body = Assert.Single(_pulls.Publications).Request.Body;
        Assert.Equal(PullRequestDescription.Compose(Facts(), Issue, Hephaestus.Core.Localization.Language.English), body);
        Assert.Contains("Issue #7", body, StringComparison.Ordinal);
        Assert.Contains("Implemented by `claude-code` (Model `claude-opus-5-5`, Effort `high`).", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_Task_moves_to_pull_request_open_with_the_result_and_the_intent_of_its_state_label_is_journaled_and_made()
    {
        var loop = await WithIntentAsync();

        await loop.TickAsync(Token);

        var task = _stores.Tasks[0];
        Assert.Equal(TaskState.PullRequestOpen, task.State);
        var label = Assert.Single(_stores.Journal, e => e.Entry == JournalEntryKind.Intent && e.Effect.Kind == EffectKind.Label && e.Effect.Cause != "g1");
        Assert.Equal(("pull-request-open", EffectKeys.StepCause(StepRunId), task.Id), (label.Effect.Detail, label.Effect.Cause, label.Effect.TaskId));
        await loop.TickAsync(Token);
        Assert.Contains("hephaestus:pull-request-open", _stores.Github.LabelsOf(task.RepositoryId, task.IssueNumber));
        Assert.DoesNotContain("hephaestus:queued", _stores.Github.LabelsOf(task.RepositoryId, task.IssueNumber));
    }

    [Fact]
    public async Task A_publication_made_and_not_recorded_is_found_by_its_head_branch_and_no_second_pull_request_is_opened()
    {
        var loop = await WithIntentAsync();
        var task = _stores.Tasks[0];
        // The host stopped after GitHub opened the pull request and before the result was recorded: the branch has its pull request, and the journal has an intent and no result.
        _pulls.Seed(task.Branch, 12);

        await NewLoop().TickAsync(Token);

        Assert.Equal(1, _pulls.OpenOf(task.Branch));
        Assert.Equal(12, _pulls.Pull(task.Branch).Number);
        Assert.Equal(EffectResult.Applied, Result().Result);
        Assert.Equal(TaskState.PullRequestOpen, _stores.Tasks[0].State);
    }

    [Fact]
    public async Task A_Task_published_again_after_further_work_has_the_same_pull_request_updated_as_an_effect_of_its_own()
    {
        var loop = await WithIntentAsync();
        await loop.TickAsync(Token);
        var task = _stores.Tasks[0];
        var first = _pulls.Pull(task.Branch).Number;
        var later = Guid.Parse("018f4b6e-0000-7000-8000-000000000009");
        await JournalAsync(loop, stepRun: later);

        await loop.TickAsync(Token);

        Assert.Equal(1, _pulls.OpenOf(task.Branch));
        Assert.Equal(first, _pulls.Pull(task.Branch).Number);
        Assert.Equal(2, _pulls.Publications.Count);
        Assert.Equal(2, _stores.Journal.Count(e => e.Entry == JournalEntryKind.Result && e.Effect.Kind == EffectKind.PullRequest));
        Assert.Equal(TaskState.PullRequestOpen, _stores.Tasks[0].State);
        Assert.Single(_stores.Journal, e => e.Entry == JournalEntryKind.Intent && e.Effect.Kind == EffectKind.Label && e.Effect.Cause != "g1");
    }

    [Fact]
    public async Task A_publication_is_an_effect_of_the_loop_with_no_step_run_and_the_Task_holds_no_slot_before_or_after()
    {
        var loop = await WithIntentAsync();

        await loop.TickAsync(Token);

        // Nothing but effects is journaled: no step run is started, and the state it leaves is not Running.
        Assert.All(_stores.Journal, entry => Assert.Contains(entry.Effect.Kind, new[] { EffectKind.PullRequest, EffectKind.Label }));
        Assert.DoesNotContain(_stores.Tasks, t => t.State == TaskState.Running);
    }

    [Fact]
    public async Task An_effect_with_a_result_is_not_made_again_by_a_later_tick_or_a_restart()
    {
        var loop = await WithIntentAsync();
        await loop.TickAsync(Token);

        await loop.TickAsync(Token);
        await NewLoop().TickAsync(Token);

        Assert.Single(_pulls.Publications);
    }

    [Theory]
    [InlineData(TaskState.Cancelled)]
    [InlineData(TaskState.Closed)]
    [InlineData(TaskState.Failed)]
    [InlineData(TaskState.Withdrawn)]
    [InlineData(TaskState.Merged)]
    [InlineData(TaskState.Queued)]
    [InlineData(TaskState.WaitingForOwner)]
    [InlineData(TaskState.WaitingForCapacity)]
    public async Task A_Task_that_has_ended_or_waits_is_published_nothing_and_its_state_is_left_as_it_is(TaskState state)
    {
        var loop = await WithIntentAsync(state);

        await loop.TickAsync(Token);

        Assert.Equal(EffectResult.Superseded, Result().Result);
        Assert.Empty(_pulls.Publications);
        Assert.Equal(state, _stores.Tasks[0].State);
    }

    [Fact]
    public async Task A_publication_that_cannot_be_made_is_retried_and_the_Task_stays_where_it_is()
    {
        _pulls.Answers.Enqueue(PullRequestResult.Failed(GitHubIssueFailure.Unreachable));
        var loop = await WithIntentAsync();

        var report = await loop.TickAsync(Token);

        Assert.Equal(1, report.EffectsRetried);
        var retry = Assert.Single(_stores.Journal, e => e.Entry == JournalEntryKind.Retry);
        Assert.Equal((1, EffectReasons.Unreachable), (retry.Failures, retry.Reason));
        Assert.Equal(TaskState.Running, _stores.Tasks[0].State);

        _time.Advance(TimeSpan.FromMinutes(5));
        await loop.TickAsync(Token);

        Assert.Equal(EffectResult.Applied, Result().Result);
        Assert.Equal(TaskState.PullRequestOpen, _stores.Tasks[0].State);
    }

    [Fact]
    public async Task A_publication_that_GitHub_refuses_for_good_ends_as_refused_and_raises_an_Escalation_whose_comment_says_the_pull_request_could_not_be_opened()
    {
        _pulls.Answers.Enqueue(PullRequestResult.Failed(GitHubIssueFailure.Rejected));
        var loop = await WithIntentAsync();

        var report = await loop.TickAsync(Token);

        Assert.Equal((EffectResult.Refused, EffectReasons.Rejected), (Result().Result, Result().Reason));
        var escalation = Assert.Single(_stores.Escalations);
        Assert.Equal((EscalationKind.EffectFailed, EffectKind.PullRequest), (escalation.Kind, escalation.Effect!.Kind));
        Assert.Equal(TaskState.WaitingForOwner, _stores.Tasks[0].State);
        Assert.True(Assert.Single(report.Problems!).MovedToWaitingForOwner);
        await loop.TickAsync(Token);
        Assert.Contains("pull request of this Task could not be opened or updated", Assert.Single(_stores.Github.Comments).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_publication_that_stays_unreachable_gives_up_at_the_bound()
    {
        _stores.Settings["effectRetries"] = "0";
        _pulls.Answers.Enqueue(PullRequestResult.Failed(GitHubIssueFailure.Unreachable));
        var loop = await WithIntentAsync();

        await loop.TickAsync(Token);

        Assert.Equal((EffectResult.GaveUp, EffectReasons.Unreachable), (Result().Result, Result().Reason));
        Assert.Equal(TaskState.WaitingForOwner, _stores.Tasks[0].State);
    }

    [Fact]
    public async Task A_wait_GitHub_asks_for_is_not_a_failure_and_the_publication_is_made_after_it()
    {
        var until = _time.Now.AddMinutes(3);
        _pulls.Answers.Enqueue(PullRequestResult.Failed(GitHubIssueFailure.RateLimited, until));
        var loop = await WithIntentAsync();

        await loop.TickAsync(Token);

        var retry = Assert.Single(_stores.Journal, e => e.Entry == JournalEntryKind.Retry);
        Assert.Equal((0, EffectReasons.RateLimited, until), (retry.Failures, retry.Reason, retry.NextAttemptAt));
        _time.Advance(TimeSpan.FromMinutes(4));
        await loop.TickAsync(Token);
        Assert.Equal(EffectResult.Applied, Result().Result);
    }

    [Fact]
    public async Task A_gateway_that_throws_is_treated_as_one_that_did_not_answer()
    {
        _pulls.Throws = new InvalidOperationException("A gateway built without the masker publishes nothing.");
        var loop = await WithIntentAsync();

        var report = await loop.TickAsync(Token);

        Assert.Equal(1, report.EffectsRetried);
        Assert.IsType<InvalidOperationException>(report.Failure);
        Assert.Equal(TaskState.Running, _stores.Tasks[0].State);
    }

    [Fact]
    public async Task A_host_with_no_gateway_for_pull_requests_refuses_the_intent()
    {
        await WithIntentAsync();

        var report = await NewLoop(withPullRequests: false).TickAsync(Token);

        Assert.Equal((EffectResult.Refused, EffectReasons.UnknownEffect), (Result().Result, Result().Reason));
        Assert.Empty(_pulls.Publications);
        Assert.Equal(EffectResult.Refused, Assert.Single(report.Problems!).Result);
    }

    [Theory]
    [InlineData("pull-request|nothing", null)]
    [InlineData("label|queued", null)]
    [InlineData(null, "not facts")]
    [InlineData(null, "")]
    public async Task An_intent_this_version_cannot_read_is_refused_and_publishes_nothing(string? detail, string? payload)
    {
        var loop = await WithIntentAsync(detail: detail, payload: payload ?? (detail is null ? "" : null));

        await loop.TickAsync(Token);

        Assert.Equal((EffectResult.Refused, EffectReasons.UnknownEffect), (Result().Result, Result().Reason));
        Assert.Empty(_pulls.Publications);
    }

    [Fact]
    public async Task A_Task_whose_branch_is_no_Task_branch_or_whose_Project_is_unknown_is_refused_before_anything_is_asked_of_GitHub()
    {
        var loop = await WithIntentAsync();
        _stores.Tasks[0] = _stores.Tasks[0] with { Branch = "main" };

        await loop.TickAsync(Token);

        Assert.Equal((EffectResult.Refused, EffectReasons.UnknownEffect), (Result().Result, Result().Reason));
        Assert.Empty(_pulls.Publications);
    }

    [Fact]
    public async Task A_Project_that_is_not_registered_is_refused()
    {
        var loop = await WithIntentAsync();
        _stores.Projects.Clear();

        await loop.TickAsync(Token);

        Assert.Equal((EffectResult.Refused, EffectReasons.UnknownEffect), (Result().Result, Result().Reason));
        Assert.Empty(_pulls.Publications);
    }

    [Fact]
    public async Task A_Project_that_was_removed_since_is_still_published_to_its_Base_Branch()
    {
        var loop = await WithIntentAsync();
        _stores.Projects[0] = _stores.Projects[0] with { BaseBranch = "develop", RemovedAt = _time.Now };

        await loop.TickAsync(Token);

        Assert.Equal("develop", Assert.Single(_pulls.Publications).Request.Base);
    }
}
