using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Loop;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>
/// Story 2.9, FR-48, AR-63, AD-1, AD-5, AD-19, UX-DR-18: an Escalation is raised through one path and recorded on the Task, the Task waits and
/// the others go on, the question is said on the issue in its fixed shape with an Agent's text neutralised, the notifier is called and changes
/// nothing, an effect that reached its bound escalates the same way, and a Trusted User can end the wait by removing the label or closing the issue.
/// </summary>
public class OrchestratorLoopEscalationTests
{
    private const long Repository = LoopStores.RepositoryId;
    private const int Issue = 7;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly LoopStores _stores = new();
    private readonly MovingTime _time = new();

    public OrchestratorLoopEscalationTests()
    {
        _stores.TimeProvider = _time;
        _stores.AppSlug = "hephaestus-test";
    }

    private OrchestratorLoop NewLoop(Hephaestus.Core.Localization.Language language = Hephaestus.Core.Localization.Language.English) =>
        new(_stores, _stores, _stores, _stores.Github, _stores.Notifier, _time, new LoopHeartbeat(_time), fallbackLanguage: language);

    private async Task<TickReport> TickWithAsync(OrchestratorLoop loop, params Signal[] signals)
    {
        await _stores.AppendAsync(signals, Token);
        return await loop.TickAsync(Token);
    }

    private static Signal Closed(string id, long? actor = 29, string? login = "alice", int issue = Issue) =>
        new(Signal.GitHubSource, id, SignalKind.Closed, Repository, issue, actor, login, null, null, null, new DateTimeOffset(2026, 11, 27, 11, 30, 0, TimeSpan.Zero));

    private async Task<(OrchestratorLoop Loop, TaskRecord Task)> RunningTaskAsync(int issue = Issue, TaskState state = TaskState.Running)
    {
        var loop = NewLoop();
        await TickWithAsync(loop, LoopStores.Labeled($"issue-event:{issue}", issue: issue));
        var index = _stores.Tasks.FindIndex(t => t.IssueNumber == issue);
        _stores.Tasks[index] = _stores.Tasks[index] with { State = state };
        return (loop, _stores.Tasks[index]);
    }

    private static EscalationRequest Question() => EscalationRequest.For("Which database should the login page use?", ["SQLite, as the rest does", "Postgres"]);

    private string[] Labels(int issue = Issue) => [.. _stores.Github.LabelsOf(Repository, issue).Order(StringComparer.Ordinal)];

    private IEnumerable<JournalEntry> Intents(EffectKind kind) => _stores.Journal.Where(e => e.Entry == JournalEntryKind.Intent && e.Effect.Kind == kind);

    [Fact]
    public async Task An_Escalation_is_recorded_on_the_Task_with_its_resume_condition_and_the_Task_moves_to_WaitingForOwner()
    {
        var (loop, task) = await RunningTaskAsync();

        Assert.True(await loop.RaiseEscalationAsync(task, Question(), "s4", Token));

        var moved = Assert.Single(_stores.Tasks);
        Assert.Equal((TaskState.WaitingForOwner, _time.Now), (moved.State, moved.UpdatedAt));
        var escalation = Assert.Single(_stores.Escalations);
        Assert.Equal((task.Id, EscalationKind.Decision, ResumeConditions.TrustedReply, "s4"), (escalation.TaskId, escalation.Kind, escalation.ResumeCondition, escalation.Cause));
        Assert.Equal("Which database should the login page use?", escalation.Decision);
        Assert.Equal(["SQLite, as the rest does", "Postgres"], escalation.Options);
        Assert.Null(escalation.ClosedAt);
    }

    [Fact]
    public async Task The_intents_of_the_label_the_comment_and_the_notification_commit_with_the_move_and_nothing_is_made_before_the_next_tick()
    {
        var (loop, task) = await RunningTaskAsync();
        var before = _stores.Github.Comments.Count;

        await loop.RaiseEscalationAsync(task, Question(), "s4", Token);

        Assert.Equal(before, _stores.Github.Comments.Count);
        Assert.Empty(_stores.Notifier.Calls);
        Assert.Equal(
            [$"{task.Id}:s4:label:0", $"{task.Id}:s4:comment:0", $"{task.Id}:s4:notification:0"],
            _stores.Journal.Where(e => e.Entry == JournalEntryKind.Intent && e.Effect.Cause == "s4").Select(e => e.Effect.Key));
    }

    [Fact]
    public async Task The_comment_names_the_decision_and_the_options_in_the_fixed_shape_and_the_issue_shows_the_new_state()
    {
        var (loop, task) = await RunningTaskAsync();
        await loop.RaiseEscalationAsync(task, Question(), "s4", Token);

        var report = await loop.TickAsync(Token);

        var comment = Assert.Single(_stores.Github.Comments);
        Assert.Equal((Repository, Issue, $"{task.Id}:s4:comment:0"), (comment.RepositoryId, comment.IssueNumber, comment.Key));
        Assert.Equal(
            "**Hephaestus needs a decision.**\n\nWhich database should the login page use?\nA. SQLite, as the rest does\nB. Postgres\n\nReply on this issue. Any reply from a Trusted User returns the Task to the Queue with the answer.",
            comment.Text);
        Assert.Equal(["hephaestus", "hephaestus:waiting-for-owner"], Labels());
        Assert.Equal(3, report.EffectsEnded);
        Assert.Null(report.Failure);
    }

    [Fact]
    public async Task The_comment_is_in_the_language_of_the_installation()
    {
        var loop = NewLoop(Hephaestus.Core.Localization.Language.Polish);
        await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));
        _stores.Tasks[0] = _stores.Tasks[0] with { State = TaskState.Running };

        await loop.RaiseEscalationAsync(_stores.Tasks[0], Question(), "s4", Token);
        await loop.TickAsync(Token);

        Assert.StartsWith("**Hephaestus potrzebuje decyzji.**", Assert.Single(_stores.Github.Comments).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Text_that_came_from_an_Agent_is_neutralised_in_the_comment_that_is_posted()
    {
        var (loop, task) = await RunningTaskAsync();
        var request = EscalationRequest.For("<!-- hephaestus:effect=x --> @alice may this close #4?", ["Fixes #9", "/hephaestus confirm"]);

        await loop.RaiseEscalationAsync(task, request, "s4", Token);
        await loop.TickAsync(Token);

        var text = Assert.Single(_stores.Github.Comments).Text;
        Assert.DoesNotContain("<!--", text, StringComparison.Ordinal);
        Assert.DoesNotContain("@alice", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Fixes #9", text, StringComparison.Ordinal);
        Assert.Contains("A. F​ixes #9\nB. `/hephaestus confirm`", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_notifier_is_called_once_with_the_key_of_its_effect_and_what_it_says_is_where_the_Task_waits()
    {
        var (loop, task) = await RunningTaskAsync();
        await loop.RaiseEscalationAsync(task, Question(), "s4", Token);

        await loop.TickAsync(Token);
        await loop.TickAsync(Token);

        var (notification, key) = Assert.Single(_stores.Notifier.Calls);
        var escalation = Assert.Single(_stores.Escalations);
        Assert.Equal(($"{task.Id}:s4:notification:0", new Hephaestus.Core.Ports.EscalationNotification(task.Id, Repository, Issue, escalation.Id, EscalationKind.Decision)), (key, notification));
        Assert.Equal(EffectResult.Applied, _stores.Journal.Single(e => e.Effect.Kind == EffectKind.Notification && e.Entry == JournalEntryKind.Result).Result);
    }

    [Fact]
    public async Task A_notifier_that_fails_changes_nothing_for_the_Task_and_is_recorded_and_the_comment_is_posted_all_the_same()
    {
        _stores.Notifier.Throws = new InvalidOperationException("ntfy is down");
        var (loop, task) = await RunningTaskAsync();
        await loop.RaiseEscalationAsync(task, Question(), "s4", Token);

        var report = await loop.TickAsync(Token);

        Assert.Equal(TaskState.WaitingForOwner, Assert.Single(_stores.Tasks).State);
        Assert.Single(_stores.Github.Comments);
        Assert.Null(report.Failure);
        var problem = Assert.Single(report.Problems!);
        Assert.Equal((task.Id, EffectResult.GaveUp, EffectReasons.NotifierFailed, false), (problem.TaskId, problem.Result, problem.Reason, problem.MovedToWaitingForOwner));
        var result = _stores.Journal.Single(e => e.Effect.Kind == EffectKind.Notification && e.Entry == JournalEntryKind.Result);
        Assert.Equal((EffectResult.GaveUp, EffectReasons.NotifierFailed), (result.Result, result.Reason));
        await loop.TickAsync(Token);
        Assert.Single(_stores.Notifier.Calls);
    }

    [Fact]
    public async Task The_outcome_is_the_same_whether_the_notifier_succeeds_fails_or_does_nothing()
    {
        var states = new List<(TaskState State, string[] Labels, int Comments)>();
        foreach (var throws in new Exception?[] { null, new InvalidOperationException("down") })
        {
            var run = new OrchestratorLoopEscalationTests();
            run._stores.Notifier.Throws = throws;
            var (loop, task) = await run.RunningTaskAsync();
            await loop.RaiseEscalationAsync(task, Question(), "s4", Token);
            await loop.TickAsync(Token);
            states.Add((Assert.Single(run._stores.Tasks).State, run.Labels(), run._stores.Github.Comments.Count));
        }

        Assert.Equal(states[0].State, states[1].State);
        Assert.Equal(states[0].Labels, states[1].Labels);
        Assert.Equal(states[0].Comments, states[1].Comments);
    }

    [Fact]
    public async Task A_notification_that_was_started_before_the_host_stopped_is_not_sent_again_and_ends_as_given_up()
    {
        var (loop, task) = await RunningTaskAsync();
        await loop.RaiseEscalationAsync(task, Question(), "s4", Token);
        var intent = Intents(EffectKind.Notification).Single();
        _stores.Journal.Add(new JournalEntry(_stores.Journal.Count + 1, JournalEntryKind.Retry, intent.Effect, null, EffectReasons.Notifying, 1, null, _time.Now));

        var report = await NewLoop().TickAsync(Token);

        Assert.Empty(_stores.Notifier.Calls);
        var result = _stores.Journal.Single(e => e.Effect.Kind == EffectKind.Notification && e.Entry == JournalEntryKind.Result);
        Assert.Equal((EffectResult.GaveUp, EffectReasons.Interrupted), (result.Result, result.Reason));
        Assert.Equal(EffectReasons.Interrupted, Assert.Single(report.Problems!).Reason);
        Assert.Single(_stores.Github.Comments);
    }

    [Fact]
    public async Task The_notification_is_journaled_as_started_before_the_call_is_made()
    {
        var (loop, task) = await RunningTaskAsync();
        await loop.RaiseEscalationAsync(task, Question(), "s4", Token);
        var seen = new List<JournalEntryKind>();
        _stores.Notifier.Before = () => seen.AddRange(_stores.Journal.Where(e => e.Effect.Kind == EffectKind.Notification).Select(e => e.Entry));

        await loop.TickAsync(Token);

        Assert.Equal([JournalEntryKind.Intent, JournalEntryKind.Retry], seen);
    }

    [Fact]
    public async Task A_second_Escalation_for_a_Task_that_waits_already_asks_nothing_and_writes_nothing()
    {
        var (loop, task) = await RunningTaskAsync();
        await loop.RaiseEscalationAsync(task, Question(), "s4", Token);
        var journal = _stores.Journal.Count;

        Assert.False(await loop.RaiseEscalationAsync(_stores.Tasks[0], Question(), "s5", Token));
        Assert.False(await loop.RaiseEscalationAsync(task, Question(), "s6", Token));

        Assert.Single(_stores.Escalations);
        Assert.Equal(journal, _stores.Journal.Count);
    }

    [Fact]
    public async Task A_Task_that_has_ended_is_not_escalated()
    {
        var (loop, task) = await RunningTaskAsync(state: TaskState.Closed);

        Assert.False(await loop.RaiseEscalationAsync(task, Question(), "s4", Token));

        Assert.Empty(_stores.Escalations);
        Assert.Equal(TaskState.Closed, Assert.Single(_stores.Tasks).State);
    }

    [Fact]
    public async Task A_Task_waiting_for_the_Owner_holds_back_no_other_Task()
    {
        var (loop, waiting) = await RunningTaskAsync(issue: 3);
        await loop.RaiseEscalationAsync(waiting, Question(), "s4", Token);

        await TickWithAsync(loop, LoopStores.Labeled("issue-event:9", issue: 9));

        Assert.Equal([TaskState.WaitingForOwner, TaskState.Queued], _stores.Tasks.Select(t => t.State));
        Assert.Equal([9], DispatchPolicy.Eligible(_stores.Tasks).Select(t => t.IssueNumber));
        Assert.Equal(["hephaestus", "hephaestus:queued"], Labels(9));
        Assert.Equal(["hephaestus", "hephaestus:waiting-for-owner"], Labels(3));
    }

    [Fact]
    public async Task A_Task_whose_label_effect_reached_its_bound_waits_through_the_same_path_and_the_comment_says_which_effect_could_not_be_completed()
    {
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.Unreachable);
        var loop = NewLoop();
        await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));
        for (var attempt = 0; attempt < 5; attempt++)
        {
            _time.Advance(TimeSpan.FromMinutes(11));
            await loop.TickAsync(Token);
        }

        // GitHub comes back: what the Escalation journaled is made after the bound, not before.
        Assert.Empty(_stores.Github.Comments);
        _stores.Github.Answer = null;
        _time.Advance(TimeSpan.FromMinutes(11));
        await loop.TickAsync(Token);

        var task = Assert.Single(_stores.Tasks);
        Assert.Equal(TaskState.WaitingForOwner, task.State);
        var escalation = Assert.Single(_stores.Escalations);
        Assert.Equal((EscalationKind.EffectFailed, ResumeConditions.TrustedReply, "g1"), (escalation.Kind, escalation.ResumeCondition, escalation.Cause));
        Assert.Equal(new FailedEffect($"{task.Id}:g1:label:0", EffectKind.Label, "queued", EffectReasons.Unreachable), escalation.Effect);
        var comment = Assert.Single(_stores.Github.Comments);
        Assert.Equal(
            "**Hephaestus needs a decision.**\n\nThe `hephaestus:queued` label could not be put on this issue, so this Task cannot go on: GitHub stayed unreachable until the limit of attempts.\n"
            + "A. Fix the cause, then reply here: the Task returns to the Queue and the change is made again.\nB. Remove the `hephaestus` label to cancel the Task.\n\n"
            + "Reply on this issue. Any reply from a Trusted User returns the Task to the Queue with the answer.",
            comment.Text);
        var (notification, _) = Assert.Single(_stores.Notifier.Calls);
        Assert.Equal((task.Id, EscalationKind.EffectFailed), (notification.TaskId, notification.Kind));
    }

    [Fact]
    public async Task A_refusal_for_good_escalates_the_same_way_and_the_comment_gives_its_reason()
    {
        _stores.Github.Answer = GitHubIssueResult.Failed(GitHubIssueFailure.Rejected);

        var loop = NewLoop();
        await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));
        await loop.TickAsync(Token);

        Assert.Equal(TaskState.WaitingForOwner, Assert.Single(_stores.Tasks).State);
        Assert.Contains("so this Task cannot go on: GitHub refused the change.\n", Assert.Single(_stores.Github.Comments).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_Escalation_raised_before_a_restart_is_made_by_a_loop_that_starts_afterwards_and_the_comment_is_posted_once()
    {
        var (loop, task) = await RunningTaskAsync();
        await loop.RaiseEscalationAsync(task, Question(), "s4", Token);

        await NewLoop().TickAsync(Token);
        await NewLoop().TickAsync(Token);

        Assert.Single(_stores.Github.Comments);
        Assert.Single(_stores.Notifier.Calls);
        Assert.Equal(["hephaestus", "hephaestus:waiting-for-owner"], Labels());
    }

    [Fact]
    public async Task A_comment_that_GitHub_refuses_leaves_the_Task_waiting_and_is_reported()
    {
        var (loop, task) = await RunningTaskAsync();
        await loop.RaiseEscalationAsync(task, Question(), "s4", Token);
        _stores.Github.CommentAnswer = GitHubCommentResult.Failed(GitHubIssueFailure.Rejected);

        var report = await loop.TickAsync(Token);

        Assert.Equal(TaskState.WaitingForOwner, Assert.Single(_stores.Tasks).State);
        var problem = Assert.Single(report.Problems!);
        Assert.Equal((task.Id, EffectResult.Refused, false), (problem.TaskId, problem.Result, problem.MovedToWaitingForOwner));
        Assert.Single(_stores.Notifier.Calls);
    }

    [Fact]
    public async Task A_Trusted_User_who_removes_the_Marker_Label_cancels_a_Task_that_waits_and_closes_its_Escalation()
    {
        var (loop, task) = await RunningTaskAsync();
        await loop.RaiseEscalationAsync(task, Question(), "s4", Token);
        await loop.TickAsync(Token);

        var report = await TickWithAsync(loop, LoopStores.Unlabeled("issue-event:2", actor: 29, login: "alice", label: "hephaestus"));

        var cancelled = Assert.Single(_stores.Tasks);
        Assert.Equal(TaskState.Cancelled, cancelled.State);
        var escalation = Assert.Single(_stores.Escalations);
        Assert.Equal((EscalationOutcome.Cancelled, _time.Now), (escalation.Outcome, escalation.ClosedAt));
        Assert.Equal(["hephaestus", "hephaestus:cancelled"], Labels());
        Assert.Equal(1, report.Applied);
    }

    [Fact]
    public async Task A_Trusted_User_who_closes_the_issue_closes_a_Task_that_waits_and_the_Escalation()
    {
        var (loop, task) = await RunningTaskAsync();
        await loop.RaiseEscalationAsync(task, Question(), "s4", Token);
        await loop.TickAsync(Token);

        await TickWithAsync(loop, Closed("issue-event:2"));

        Assert.Equal(TaskState.Closed, Assert.Single(_stores.Tasks).State);
        Assert.Equal(EscalationOutcome.Closed, Assert.Single(_stores.Escalations).Outcome);
        Assert.Equal(["hephaestus", "hephaestus:closed"], Labels());
    }

    [Theory]
    [InlineData(1001L, "bob")]
    [InlineData(null, null)]
    [InlineData(29L, "hephaestus-test[bot]")]
    public async Task Anyone_else_who_removes_the_label_or_closes_the_issue_changes_nothing(long? actor, string? login)
    {
        var (loop, task) = await RunningTaskAsync();
        await loop.RaiseEscalationAsync(task, Question(), "s4", Token);
        await loop.TickAsync(Token);

        await TickWithAsync(loop, LoopStores.Unlabeled("issue-event:2", actor, login, "hephaestus"), Closed("issue-event:3", actor, login));

        Assert.Equal(TaskState.WaitingForOwner, Assert.Single(_stores.Tasks).State);
        Assert.Null(Assert.Single(_stores.Escalations).ClosedAt);
        Assert.All(_stores.Signals, s => Assert.Equal(SignalStatus.Applied, s.Status));
    }

    [Theory]
    [InlineData(TaskState.Queued)]
    [InlineData(TaskState.Running)]
    [InlineData(TaskState.Merged)]
    public async Task A_Task_that_does_not_wait_is_not_moved_by_this_story_the_other_rows_of_the_table_are_Story_2_3(TaskState state)
    {
        var (loop, _) = await RunningTaskAsync(state: state);

        await TickWithAsync(loop, LoopStores.Unlabeled("issue-event:2", 29, "alice", "hephaestus"), Closed("issue-event:3"));

        Assert.Equal(state, Assert.Single(_stores.Tasks).State);
    }

    [Fact]
    public async Task A_removal_or_a_close_on_an_issue_that_is_no_Task_or_in_no_Project_is_applied_with_no_change()
    {
        var loop = NewLoop();

        await TickWithAsync(loop, LoopStores.Unlabeled("issue-event:1", 29, "alice", "hephaestus", issue: 40), Closed("issue-event:2", issue: 41));
        _stores.Projects.Clear();
        await TickWithAsync(loop, Closed("issue-event:3", issue: 42));

        Assert.Empty(_stores.Tasks);
        Assert.All(_stores.Signals, s => Assert.Equal(SignalStatus.Applied, s.Status));
    }

    [Fact]
    public async Task A_Task_that_was_cancelled_while_it_waited_can_be_escalated_again_after_it_is_queued_again()
    {
        var (loop, task) = await RunningTaskAsync();
        await loop.RaiseEscalationAsync(task, Question(), "s4", Token);
        await TickWithAsync(loop, LoopStores.Unlabeled("issue-event:2", 29, "alice", "hephaestus"));
        _stores.Tasks[0] = _stores.Tasks[0] with { State = TaskState.Running };

        Assert.True(await loop.RaiseEscalationAsync(_stores.Tasks[0], Question(), "s9", Token));

        Assert.Equal([EscalationOutcome.Cancelled, null], _stores.Escalations.Select(e => e.Outcome));
    }
}
