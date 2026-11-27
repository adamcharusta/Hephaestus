using Hephaestus.Core.Effects;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Loop;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>
/// Story 2.4, FR-11, AD-2, AD-5: a Task's state is shown on its issue as one label, the label is put back when it is changed by hand, and every
/// change is journaled as an intent with the state change and a result after it, retried within a bound, and resolved by reading after a crash.
/// </summary>
public class OrchestratorLoopEffectTests
{
    private const long Repository = LoopStores.RepositoryId;
    private const int Issue = 7;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly LoopStores _stores = new();
    private readonly MovingTime _time = new();

    public OrchestratorLoopEffectTests()
    {
        _stores.TimeProvider = _time;
        _stores.AppSlug = "hephaestus-test";
    }

    private OrchestratorLoop NewLoop() => new(_stores, _stores, _stores, _stores.Github, _time, new LoopHeartbeat(_time));

    private async Task<TickReport> TickWithAsync(OrchestratorLoop loop, params Signal[] signals)
    {
        await _stores.AppendAsync(signals, Token);
        return await loop.TickAsync(Token);
    }

    private static string[] Sorted(IEnumerable<string> labels) => [.. labels.Order(StringComparer.Ordinal)];

    private string[] Labels(int issue = Issue) => Sorted(_stores.Github.LabelsOf(Repository, issue));

    private IReadOnlyList<JournalEntry> Journal() => _stores.Journal;

    private Guid TaskId => Assert.Single(_stores.Tasks).Id;

    private async Task<OrchestratorLoop> OptedInAsync()
    {
        var loop = NewLoop();
        await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));
        return loop;
    }

    [Fact]
    public async Task A_Task_that_enters_Queued_has_the_issue_carry_exactly_one_state_label_after_the_next_cycle()
    {
        var report = await TickWithAsync(NewLoop(), LoopStores.Labeled("issue-event:1"));

        Assert.Equal(["hephaestus", "hephaestus:queued"], Labels());
        Assert.Equal(1, report.EffectsEnded);
        Assert.Null(report.Failure);
        Assert.Empty(report.Problems ?? []);
    }

    [Fact]
    public async Task The_intent_commits_in_the_same_transaction_as_the_state_change_and_names_its_cause_by_the_Signals_sequence()
    {
        await OptedInAsync();

        var application = Assert.Single(_stores.Applications);
        var effect = Assert.Single(application.NewEffects!);
        Assert.Equal(new NewEffect(TaskId, "g1", EffectKind.Label, "queued"), effect);
        var intent = Journal()[0];
        Assert.Equal(JournalEntryKind.Intent, intent.Entry);
        Assert.Equal($"{TaskId}:g1:label:0", intent.Effect.Key);
        Assert.Equal("g1", intent.Effect.Cause);
        Assert.Equal(0, intent.Effect.Ordinal);
    }

    [Fact]
    public async Task The_port_method_that_made_the_change_received_the_key_the_journal_holds_and_the_result_is_recorded_after_it()
    {
        await OptedInAsync();

        var change = Assert.Single(_stores.Github.Changes);
        Assert.Equal($"{TaskId}:g1:label:0", change.Key);
        Assert.Equal((Repository, Issue), (change.RepositoryId, change.IssueNumber));
        Assert.Equal(["hephaestus:queued"], change.Add);
        Assert.Empty(change.Remove);
        Assert.Equal([JournalEntryKind.Intent, JournalEntryKind.Result], Journal().Select(e => e.Entry));
        var result = Journal()[1];
        Assert.Equal((EffectResult.Applied, null), (result.Result, result.Reason));
        Assert.Equal(change.Key, result.Effect.Key);
    }

    [Fact]
    public async Task An_issue_that_carries_another_state_label_is_made_to_carry_only_the_right_one_and_other_labels_are_left()
    {
        _stores.Github.LabelsOf(Repository, Issue).AddRange(["hephaestus:running", "bug"]);

        await OptedInAsync();

        Assert.Equal(["bug", "hephaestus", "hephaestus:queued"], Labels());
        var change = Assert.Single(_stores.Github.Changes);
        Assert.Equal(["hephaestus:queued"], change.Add);
        Assert.Equal(["hephaestus:running"], change.Remove);
    }

    [Fact]
    public async Task A_second_tick_with_nothing_new_makes_no_second_change_and_asks_no_question()
    {
        var loop = await OptedInAsync();
        var reads = _stores.Github.Reads;

        var report = await loop.TickAsync(Token);

        Assert.Equal(reads, _stores.Github.Reads);
        Assert.Single(_stores.Github.Changes);
        Assert.Equal(0, report.EffectsEnded);
    }

    [Theory]
    [InlineData("hephaestus:queued")]
    [InlineData("Hephaestus:Queued")]
    public async Task A_state_label_that_someone_removes_by_hand_is_written_back_when_the_loop_next_reconciles(string label)
    {
        var loop = await OptedInAsync();
        _stores.Github.LabelsOf(Repository, Issue).Remove("hephaestus:queued");

        await TickWithAsync(loop, LoopStores.Unlabeled("issue-event:2", label: label));

        Assert.Equal(["hephaestus", "hephaestus:queued"], Labels());
        Assert.Equal(2, _stores.Github.Changes.Count);
        var second = Journal().Where(e => e.Entry == JournalEntryKind.Intent).ElementAt(1);
        Assert.Equal($"{TaskId}:g2:label:0", second.Effect.Key);
        Assert.Equal("queued", second.Effect.Detail);
    }

    [Fact]
    public async Task A_state_label_that_someone_replaces_by_hand_with_another_is_replaced_back()
    {
        var loop = await OptedInAsync();
        var labels = _stores.Github.LabelsOf(Repository, Issue);
        labels.Remove("hephaestus:queued");
        labels.Add("hephaestus:merged");

        await TickWithAsync(loop, LoopStores.Unlabeled("issue-event:2"), LoopStores.Labeled("issue-event:3", actor: 1001, login: "bob", label: "hephaestus:merged"));

        Assert.Equal(["hephaestus", "hephaestus:queued"], Labels());
        Assert.Equal(["hephaestus:queued"], _stores.Github.Changes[1].Add);
        Assert.Equal(["hephaestus:merged"], _stores.Github.Changes[1].Remove);
    }

    [Fact]
    public async Task A_flood_of_changes_by_hand_to_one_issue_is_answered_by_one_effect_that_reads_the_labels_as_they_are_then()
    {
        var loop = await OptedInAsync();
        _stores.Github.LabelsOf(Repository, Issue).Remove("hephaestus:queued");

        var report = await TickWithAsync(
            loop,
            LoopStores.Unlabeled("issue-event:2"),
            LoopStores.Labeled("issue-event:3", actor: 1001, login: "bob", label: "hephaestus:running"),
            LoopStores.Unlabeled("issue-event:4", label: "hephaestus:running"),
            LoopStores.Unlabeled("issue-event:5"));

        Assert.Equal(["hephaestus", "hephaestus:queued"], Labels());
        Assert.Equal(4, report.Applied);
        Assert.Equal(2, _stores.Github.Changes.Count);
        Assert.Equal(2, Journal().Count(e => e.Entry == JournalEntryKind.Intent));
        Assert.Equal([EffectResult.Applied, EffectResult.Applied], Journal().Where(e => e.Result is not null).Select(e => e.Result!.Value));
    }

    [Fact]
    public async Task A_change_by_hand_while_an_effect_for_the_Task_is_still_waiting_to_be_tried_again_is_answered_by_that_effect()
    {
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.Unreachable);
        var loop = NewLoop();
        await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));

        await TickWithAsync(loop, LoopStores.Unlabeled("issue-event:2"), LoopStores.Labeled("issue-event:3", actor: 1001, login: "bob", label: "hephaestus:merged"));

        Assert.Equal(1, Journal().Count(e => e.Entry == JournalEntryKind.Intent));
    }

    [Fact]
    public async Task A_change_by_hand_to_another_Tasks_issue_is_not_covered_by_the_first_Tasks_effect()
    {
        var loop = NewLoop();
        await TickWithAsync(loop, LoopStores.Labeled("issue-event:1", issue: 8));
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.Unreachable);
        await TickWithAsync(loop, LoopStores.Labeled("issue-event:2", issue: 7));

        await TickWithAsync(loop, LoopStores.Unlabeled("issue-event:3", issue: 8));

        Assert.Equal(3, Journal().Count(e => e.Entry == JournalEntryKind.Intent));
    }

    [Fact]
    public async Task What_Hephaestus_wrote_itself_is_not_answered_so_that_it_does_not_answer_its_own_change_for_ever()
    {
        var loop = await OptedInAsync();

        var report = await TickWithAsync(loop, LoopStores.Labeled("issue-event:2", actor: 99999, login: "hephaestus-test[bot]", label: "hephaestus:queued"));

        Assert.Equal(1, report.Applied);
        Assert.Single(_stores.Github.Changes);
        Assert.Equal(2, Journal().Count);
    }

    [Fact]
    public async Task A_state_label_by_someone_with_no_actor_or_no_trust_is_put_right_all_the_same_because_it_is_a_projection_and_not_an_intent()
    {
        var loop = await OptedInAsync();
        _stores.Github.LabelsOf(Repository, Issue).Add("hephaestus:running");

        await TickWithAsync(loop, LoopStores.Labeled("issue-event:2", actor: null, login: null, label: "hephaestus:running"));

        Assert.Equal(["hephaestus", "hephaestus:queued"], Labels());
        Assert.Equal(TaskState.Queued, Assert.Single(_stores.Tasks).State);
    }

    [Fact]
    public async Task A_state_label_on_an_issue_that_is_not_a_Task_is_nobodys_business()
    {
        var report = await TickWithAsync(NewLoop(), LoopStores.Labeled("issue-event:1", label: "hephaestus:running", issue: 9));

        Assert.Equal((1, 0), (report.Applied, report.Rejected));
        Assert.Empty(Journal());
        Assert.Empty(_stores.Github.Changes);
    }

    [Fact]
    public async Task A_label_that_is_neither_the_marker_nor_a_state_label_changes_nothing()
    {
        var loop = await OptedInAsync();

        await TickWithAsync(loop, LoopStores.Labeled("issue-event:2", label: "bug"), LoopStores.Unlabeled("issue-event:3", label: "hephaestus:bogus"));

        Assert.Equal(2, Journal().Count);
    }

    [Fact]
    public async Task A_crash_after_the_intent_and_before_the_result_is_resolved_by_reading_the_issue_and_the_label_is_applied_exactly_once()
    {
        // The change reached GitHub and the host stopped before the result was recorded.
        _stores.FailToRecord = new InvalidOperationException("The host stopped.");
        var crashed = await TickWithAsync(NewLoop(), LoopStores.Labeled("issue-event:1"));
        _stores.FailToRecord = null;
        Assert.NotNull(crashed.Failure);
        Assert.Equal([JournalEntryKind.Intent], Journal().Select(e => e.Entry));
        Assert.Single(_stores.Github.Changes);

        var restarted = NewLoop();
        var report = await restarted.TickAsync(Token);
        await restarted.TickAsync(Token);

        Assert.Single(_stores.Github.Changes);
        Assert.Equal(["hephaestus", "hephaestus:queued"], Labels());
        Assert.Equal([JournalEntryKind.Intent, JournalEntryKind.Result], Journal().Select(e => e.Entry));
        Assert.Equal(EffectResult.AlreadyInPlace, Journal()[1].Result);
        Assert.Equal(1, report.EffectsEnded);
    }

    [Fact]
    public async Task A_crash_before_the_label_was_made_has_the_first_tick_make_it_once_and_a_further_restart_makes_nothing()
    {
        _stores.FailToRecord = new InvalidOperationException("The host stopped.");
        _stores.Github.Throws = new InvalidOperationException("The host stopped.");
        await TickWithAsync(NewLoop(), LoopStores.Labeled("issue-event:1"));
        _stores.FailToRecord = null;
        _stores.Github.Throws = null;
        Assert.Equal([JournalEntryKind.Intent], Journal().Select(e => e.Entry));
        Assert.Empty(_stores.Github.Changes);

        await NewLoop().TickAsync(Token);
        await NewLoop().TickAsync(Token);

        Assert.Single(_stores.Github.Changes);
        Assert.Equal(["hephaestus", "hephaestus:queued"], Labels());
        Assert.Equal([JournalEntryKind.Intent, JournalEntryKind.Result], Journal().Select(e => e.Entry));
        Assert.Equal(EffectResult.Applied, Journal()[1].Result);
    }

    [Fact]
    public async Task A_change_that_was_made_but_whose_answer_was_lost_is_found_by_reading_on_the_next_attempt_and_not_made_again()
    {
        _stores.Github.ChangeAnswers.Enqueue(GitHubIssueResult.Failed(GitHubIssueFailure.Unreachable));
        var loop = NewLoop();
        await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));
        Assert.Equal([JournalEntryKind.Intent, JournalEntryKind.Retry], Journal().Select(e => e.Entry));

        _time.Advance(TimeSpan.FromSeconds(31));
        var report = await loop.TickAsync(Token);

        Assert.Single(_stores.Github.Changes);
        Assert.Equal(EffectResult.AlreadyInPlace, Journal()[^1].Result);
        Assert.Equal(1, report.EffectsEnded);
    }

    [Fact]
    public async Task GitHub_that_is_unreachable_leaves_the_Task_in_its_state_and_the_change_is_retried_at_a_recorded_next_attempt_time()
    {
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.Unreachable);
        var loop = NewLoop();

        var report = await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));

        Assert.Equal(TaskState.Queued, Assert.Single(_stores.Tasks).State);
        var retry = Journal()[1];
        Assert.Equal((JournalEntryKind.Retry, null, "unreachable", 1), (retry.Entry, retry.Result, retry.Reason, retry.Failures));
        Assert.Equal(_time.Now + TimeSpan.FromSeconds(30), retry.NextAttemptAt);
        Assert.Equal((0, 1), (report.EffectsEnded, report.EffectsRetried));
        Assert.Null(report.Failure);
    }

    [Fact]
    public async Task Nothing_is_attempted_before_the_recorded_time_and_the_wait_grows_with_each_failure()
    {
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.Unreachable);
        var loop = NewLoop();
        await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));
        var reads = _stores.Github.Reads;

        _time.Advance(TimeSpan.FromSeconds(29));
        await loop.TickAsync(Token);
        Assert.Equal(reads, _stores.Github.Reads);

        _time.Advance(TimeSpan.FromSeconds(1));
        await loop.TickAsync(Token);

        Assert.Equal(reads + 1, _stores.Github.Reads);
        var retries = Journal().Where(e => e.Entry == JournalEntryKind.Retry).ToList();
        Assert.Equal([1, 2], retries.Select(e => e.Failures));
        Assert.Equal(_time.Now + TimeSpan.FromSeconds(60), retries[1].NextAttemptAt);
    }

    [Fact]
    public async Task When_GitHub_comes_back_within_the_bound_the_label_is_made_and_the_Task_never_left_its_state()
    {
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.Unreachable);
        var loop = NewLoop();
        await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));
        _stores.Github.Answer = null;

        _time.Advance(TimeSpan.FromSeconds(30));
        await loop.TickAsync(Token);

        Assert.Equal(["hephaestus", "hephaestus:queued"], Labels());
        Assert.Equal(TaskState.Queued, Assert.Single(_stores.Tasks).State);
        Assert.Equal(EffectResult.Applied, Journal()[^1].Result);
    }

    [Fact]
    public async Task At_the_bound_the_Task_moves_to_WaitingForOwner_and_not_to_Failed_and_the_move_shows_on_the_issue_when_GitHub_returns()
    {
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.Unreachable);
        var loop = NewLoop();
        await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));
        TickReport last = null!;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            _time.Advance(TimeSpan.FromMinutes(11));
            last = await loop.TickAsync(Token);
        }

        var task = Assert.Single(_stores.Tasks);
        Assert.Equal(TaskState.WaitingForOwner, task.State);
        Assert.Equal(_time.Now, task.UpdatedAt);
        var ended = Journal().Single(e => e.Result is not null);
        Assert.Equal((EffectResult.GaveUp, "unreachable", 6), (ended.Result, ended.Reason, ended.Failures));
        var problem = Assert.Single(last.Problems!);
        Assert.Equal((task.Id, EffectResult.GaveUp, "unreachable", true), (problem.TaskId, problem.Result, problem.Reason, problem.MovedToWaitingForOwner));
        var next = Journal().Last(e => e.Entry == JournalEntryKind.Intent);
        Assert.Equal(($"{task.Id}:g1:label:1", "waiting-for-owner"), (next.Effect.Key, next.Effect.Detail));

        _stores.Github.Answer = null;
        _time.Advance(TimeSpan.FromMinutes(11));
        await loop.TickAsync(Token);
        Assert.Equal(["hephaestus", "hephaestus:waiting-for-owner"], Labels());
    }

    [Fact]
    public async Task A_bound_of_zero_retries_gives_the_Task_up_to_the_Owner_at_the_first_failure()
    {
        _stores.Settings["effectRetries"] = "0";
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.Unreachable);

        var report = await TickWithAsync(NewLoop(), LoopStores.Labeled("issue-event:1"));

        Assert.Equal(TaskState.WaitingForOwner, Assert.Single(_stores.Tasks).State);
        Assert.Equal(EffectResult.GaveUp, Assert.Single(report.Problems!).Result);
    }

    [Fact]
    public async Task A_bound_the_Owner_raised_is_the_one_used()
    {
        _stores.Settings["effectRetries"] = "7";
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.Unreachable);
        var loop = NewLoop();
        await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));

        for (var attempt = 0; attempt < 6; attempt++)
        {
            _time.Advance(TimeSpan.FromMinutes(11));
            await loop.TickAsync(Token);
        }

        Assert.Equal(TaskState.Queued, Assert.Single(_stores.Tasks).State);
        Assert.Equal(7, Journal().Count(e => e.Entry == JournalEntryKind.Retry));
    }

    [Fact]
    public async Task An_effect_that_GitHub_refuses_for_good_is_not_retried_and_the_refusal_is_journaled_with_its_reason()
    {
        _stores.Github.Missing.Add((Repository, Issue));
        var loop = NewLoop();

        var report = await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));
        _time.Advance(TimeSpan.FromHours(1));
        await loop.TickAsync(Token);

        var task = Assert.Single(_stores.Tasks);
        Assert.Equal(TaskState.WaitingForOwner, task.State);
        var refusal = Journal().First(e => e.Result is not null);
        Assert.Equal((EffectResult.Refused, "issue-not-found", 0), (refusal.Result, refusal.Reason, refusal.Failures));
        Assert.DoesNotContain(Journal(), e => e.Entry == JournalEntryKind.Retry);
        var problem = Assert.Single(report.Problems!);
        Assert.Equal((task.Id, "issue-not-found"), (problem.TaskId, problem.Reason));
        Assert.Empty(_stores.Github.Changes);
    }

    [Theory]
    [InlineData(GitHubIssueFailure.NotInstalled, "app-not-installed")]
    [InlineData(GitHubIssueFailure.Rejected, "rejected")]
    [InlineData(GitHubIssueFailure.IssueNotFound, "issue-not-found")]
    public async Task Each_refusal_has_its_own_reason_and_ends_the_effect_at_once(GitHubIssueFailure failure, string reason)
    {
        _stores.Github.Answer = GitHubIssueResult.Failed(failure);

        var report = await TickWithAsync(NewLoop(), LoopStores.Labeled("issue-event:1"));

        var problem = Assert.Single(report.Problems!);
        Assert.Equal((EffectResult.Refused, reason), (problem.Result, problem.Reason));
        Assert.Equal(TaskState.WaitingForOwner, Assert.Single(_stores.Tasks).State);
    }

    [Fact]
    public async Task The_label_that_shows_the_move_to_WaitingForOwner_that_is_refused_too_ends_there_and_moves_the_Task_nowhere_else()
    {
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.Rejected);
        var loop = NewLoop();

        await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));
        var report = await loop.TickAsync(Token);

        Assert.Equal(TaskState.WaitingForOwner, Assert.Single(_stores.Tasks).State);
        Assert.Equal(2, Journal().Count(e => e.Result == EffectResult.Refused));
        var problem = Assert.Single(report.Problems!);
        Assert.False(problem.MovedToWaitingForOwner);
        Assert.Empty(_stores.Github.Changes);
        Assert.Equal(2, Journal().Count(e => e.Entry == JournalEntryKind.Intent));
    }

    [Theory]
    [InlineData(TaskState.Cancelled)]
    [InlineData(TaskState.Merged)]
    [InlineData(TaskState.Closed)]
    public async Task A_Task_that_has_ended_is_not_resurrected_by_an_effect_that_cannot_be_made(TaskState state)
    {
        var loop = await OptedInAsync();
        _stores.Tasks[0] = _stores.Tasks[0] with { State = state };
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.Rejected);

        var report = await TickWithAsync(loop, LoopStores.Unlabeled("issue-event:2", label: $"hephaestus:{state.Tag()}"));

        Assert.Equal(state, Assert.Single(_stores.Tasks).State);
        Assert.False(Assert.Single(report.Problems!).MovedToWaitingForOwner);
        Assert.Equal(2, Journal().Count(e => e.Entry == JournalEntryKind.Intent));
    }

    [Fact]
    public async Task Two_effects_of_one_Task_that_are_due_in_one_tick_when_the_first_moves_the_Task_find_the_second_for_a_state_it_has_left()
    {
        // The second effect was journaled by a hand-made change while the first waited; both are due in the same tick.
        var loop = await OptedInAsync();
        var queued = _stores.Journal.Single(e => e.Entry == JournalEntryKind.Intent).Effect;
        var second = new Effect($"{queued.TaskId}:g2:label:0", queued.TaskId, "g2", EffectKind.Label, "queued", 0);
        _stores.Journal.Add(new JournalEntry(_stores.Journal.Count + 1, JournalEntryKind.Intent, second, null, null, 0, null, _time.Now));
        _stores.Journal.RemoveAll(e => e.Entry == JournalEntryKind.Result);
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.Rejected);

        var report = await loop.TickAsync(Token);

        Assert.Equal(TaskState.WaitingForOwner, Assert.Single(_stores.Tasks).State);
        Assert.True(Assert.Single(report.Problems!).MovedToWaitingForOwner);
        Assert.Equal(1, Journal().Count(e => e.Entry == JournalEntryKind.Intent && e.Effect.Detail == "waiting-for-owner"));
        Assert.Equal([EffectResult.Refused, EffectResult.Superseded], Journal().Where(e => e.Result is not null).Select(e => e.Result!.Value));
    }

    [Fact]
    public async Task An_effect_for_a_state_the_Task_has_left_is_superseded_and_changes_nothing()
    {
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.Unreachable);
        var loop = NewLoop();
        await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));
        _stores.Github.Answer = null;
        _stores.Tasks[0] = _stores.Tasks[0] with { State = TaskState.Running };

        _time.Advance(TimeSpan.FromSeconds(30));
        await loop.TickAsync(Token);

        Assert.Equal(EffectResult.Superseded, Journal()[^1].Result);
        Assert.Empty(_stores.Github.Changes);
        Assert.Equal(["hephaestus"], Labels());
    }

    [Fact]
    public async Task A_wait_that_GitHub_asks_for_is_kept_without_counting_a_failure()
    {
        var until = _time.Now.AddMinutes(45);
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.RateLimited, until);
        var loop = NewLoop();

        await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));

        var retry = Journal()[1];
        Assert.Equal(("rate-limited", 0, until), (retry.Reason, retry.Failures, retry.NextAttemptAt));
        Assert.Equal(TaskState.Queued, Assert.Single(_stores.Tasks).State);
    }

    [Fact]
    public async Task A_rate_limit_that_states_no_time_waits_one_minute_and_is_tried_again_after_it()
    {
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.RateLimited);
        var loop = NewLoop();
        await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));
        Assert.Equal(_time.Now.AddMinutes(1), Journal()[1].NextAttemptAt);
        _stores.Github.Answer = null;

        _time.Advance(TimeSpan.FromMinutes(1));
        await loop.TickAsync(Token);

        Assert.Equal(["hephaestus", "hephaestus:queued"], Labels());
    }

    [Theory]
    [InlineData(GitHubIssueFailure.NotConnected, "not-connected")]
    [InlineData(GitHubIssueFailure.KeyUnreadable, "key-unreadable")]
    public async Task A_credential_the_Owner_can_mend_is_retried_within_the_bound_with_its_own_reason(GitHubIssueFailure failure, string reason)
    {
        _stores.Github.Answer = GitHubIssueResult.Failed(failure);

        await TickWithAsync(NewLoop(), LoopStores.Labeled("issue-event:1"));

        Assert.Equal(reason, Journal()[1].Reason);
        Assert.Equal(TaskState.Queued, Assert.Single(_stores.Tasks).State);
    }

    [Fact]
    public async Task A_gateway_that_throws_is_taken_for_one_that_did_not_answer_and_the_tick_reports_why()
    {
        _stores.Github.Throws = new InvalidOperationException("The gateway broke.");

        var report = await TickWithAsync(NewLoop(), LoopStores.Labeled("issue-event:1"));

        Assert.IsType<InvalidOperationException>(report.Failure);
        Assert.Equal("unreachable", Journal()[1].Reason);
        Assert.Equal(TaskState.Queued, Assert.Single(_stores.Tasks).State);
    }

    [Fact]
    public async Task When_GitHub_does_not_answer_the_other_effects_wait_so_that_they_do_not_spend_their_retries_for_nothing()
    {
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.Unreachable);

        await TickWithAsync(
            NewLoop(),
            LoopStores.Labeled("issue-event:1", issue: 7),
            LoopStores.Labeled("issue-event:2", issue: 8),
            LoopStores.Labeled("issue-event:3", issue: 9));

        Assert.Equal(1, Journal().Count(e => e.Entry == JournalEntryKind.Retry));
        Assert.Equal(3, Journal().Count(e => e.Entry == JournalEntryKind.Intent));
    }

    [Fact]
    public async Task One_Task_whose_issue_is_gone_does_not_hold_back_the_others()
    {
        _stores.Github.Missing.Add((Repository, 7));

        var report = await TickWithAsync(
            NewLoop(),
            LoopStores.Labeled("issue-event:1", issue: 7),
            LoopStores.Labeled("issue-event:2", issue: 8));

        Assert.Equal(["hephaestus", "hephaestus:queued"], Labels(8));
        Assert.Equal(2, report.EffectsEnded);
        Assert.Equal([TaskState.WaitingForOwner, TaskState.Queued], _stores.Tasks.Select(t => t.State));
    }

    [Fact]
    public async Task One_tick_attempts_at_most_a_bounded_number_of_effects_and_the_rest_follow_on_the_next_ticks()
    {
        var loop = NewLoop();
        var signals = Enumerable.Range(1, OrchestratorLoop.MaximumEffectsPerTick + 5)
            .Select(n => LoopStores.Labeled($"issue-event:{n}", issue: 100 + n))
            .ToArray();
        await _stores.AppendAsync(signals, Token);

        var first = await loop.TickAsync(Token);
        var second = await loop.TickAsync(Token);

        Assert.Equal(OrchestratorLoop.MaximumEffectsPerTick, first.EffectsEnded);
        Assert.Equal(5, second.EffectsEnded);
    }

    [Fact]
    public async Task A_failure_to_record_what_happened_is_a_failure_of_the_tick_and_the_effect_is_tried_again_by_reading_first()
    {
        _stores.FailToRecord = new InvalidOperationException("The database is not there.");
        var loop = NewLoop();

        var first = await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));
        _stores.FailToRecord = null;
        var second = await loop.TickAsync(Token);

        Assert.IsType<InvalidOperationException>(first.Failure);
        Assert.Single(_stores.Github.Changes);
        Assert.Equal(EffectResult.AlreadyInPlace, Journal()[^1].Result);
        Assert.Null(second.Failure);
    }

    [Fact]
    public async Task The_problems_found_before_a_failure_to_record_another_are_still_reported_by_the_tick()
    {
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.Rejected);
        await _stores.AppendAsync([LoopStores.Labeled("issue-event:1", issue: 7), LoopStores.Labeled("issue-event:2", issue: 8)], Token);
        var recordings = 0;
        _stores.OnRecord = () =>
        {
            if (++recordings == 2)
            {
                throw new InvalidOperationException("The database is not there.");
            }
        };

        var report = await NewLoop().TickAsync(Token);

        Assert.IsType<InvalidOperationException>(report.Failure);
        Assert.Equal(TaskState.WaitingForOwner, _stores.Tasks[0].State);
        Assert.True(Assert.Single(report.Problems!).MovedToWaitingForOwner);
    }

    [Fact]
    public async Task A_Signal_that_fails_to_apply_does_not_hold_back_the_effects_that_are_due()
    {
        var loop = NewLoop();
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.Unreachable);
        await TickWithAsync(loop, LoopStores.Labeled("issue-event:1", issue: 7));
        _stores.Github.Answer = null;
        _time.Advance(TimeSpan.FromSeconds(30));
        await _stores.AppendAsync([LoopStores.Labeled("issue-event:2", issue: 8)], Token);
        _stores.Failures[2] = 1;

        var report = await loop.TickAsync(Token);

        Assert.IsType<InvalidOperationException>(report.Failure);
        Assert.Equal(["hephaestus", "hephaestus:queued"], Labels(7));
    }

    [Fact]
    public async Task Every_line_of_the_journal_names_the_Task_and_what_caused_it()
    {
        var loop = await OptedInAsync();
        _stores.Github.LabelsOf(Repository, Issue).Remove("hephaestus:queued");
        await TickWithAsync(loop, LoopStores.Unlabeled("issue-event:2"));

        Assert.All(Journal(), entry =>
        {
            Assert.Equal(TaskId, entry.Effect.TaskId);
            Assert.Matches("^g[0-9]+$", entry.Effect.Cause);
            Assert.StartsWith($"{TaskId}:{entry.Effect.Cause}:label:", entry.Effect.Key, StringComparison.Ordinal);
        });
        Assert.Equal(["g1", "g1", "g2", "g2"], Journal().Select(e => e.Effect.Cause));
    }
}
