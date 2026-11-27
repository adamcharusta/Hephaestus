using Hephaestus.Core.Loop;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Trust;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>Story 2.2, AD-1, AD-2, AD-15: a Trusted User's Marker Label creates a Task, in one transaction per Signal, and nothing else does.</summary>
public class OrchestratorLoopTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly LoopStores _stores = new();
    private readonly MovingTime _time = new();

    public OrchestratorLoopTests()
    {
        _stores.TimeProvider = _time;
    }

    private OrchestratorLoop Loop(List<TimeSpan>? waits = null, Action? onDelay = null) =>
        new(_stores, _stores, _stores, _stores.Github, _time, new LoopHeartbeat(_time), (wait, _) =>
        {
            waits?.Add(wait);
            onDelay?.Invoke();
            return Task.CompletedTask;
        });

    private async Task<TickReport> AppendAndTickAsync(params Signal[] signals)
    {
        await _stores.AppendAsync(signals, Token);
        return await Loop().TickAsync(Token);
    }

    [Fact]
    public async Task A_Trusted_Users_Marker_Label_creates_a_Queued_Task_for_that_Project_and_issue()
    {
        var report = await AppendAndTickAsync(LoopStores.Labeled("issue-event:1", issue: 7));

        var task = Assert.Single(_stores.Tasks);
        Assert.Equal((LoopStores.RepositoryId, 7, TaskState.Queued), (task.RepositoryId, task.IssueNumber, task.State));
        Assert.True(EntityIdIsVersion7(task.Id));
        Assert.Equal(_time.Now, task.CreatedAt);
        Assert.Equal(_time.Now, task.UpdatedAt);
        AssertTick(report, 1, 0, 1);
    }

    private static void AssertTick(TickReport report, int applied, int rejected, int created, params string[] rejections)
    {
        Assert.Equal((applied, rejected, created), (report.Applied, report.Rejected, report.TasksCreated));
        Assert.Null(report.Failure);
        Assert.Equal(rejections, report.Rejections);
    }

    private static bool EntityIdIsVersion7(Guid id) => Hephaestus.Core.Identity.EntityId.IsValid(id);

    [Fact]
    public async Task The_signal_is_applied_in_one_commit_that_names_the_Task_it_made()
    {
        await AppendAndTickAsync(LoopStores.Labeled("issue-event:1"));

        var application = Assert.Single(_stores.Applications);
        var task = Assert.Single(_stores.Tasks);
        Assert.Equal(SignalStatus.Applied, application.Status);
        Assert.Equal(task, application.NewTask?.Task);
        Assert.Equal(task.Id, application.TaskId);
        var stored = Assert.Single(_stores.Signals);
        Assert.Equal((SignalStatus.Applied, task.Id), (stored.Status, stored.TaskId));
    }

    [Fact]
    public async Task The_issues_title_and_body_are_snapshotted_with_their_hash_as_the_event_vouched_for_them()
    {
        await AppendAndTickAsync(LoopStores.Labeled("issue-event:1", title: "Add a login page", body: "Use OAuth.\r\nNo passwords."));

        var snapshot = await _stores.FindIssueSnapshotAsync(Assert.Single(_stores.Tasks).Id, Token);

        Assert.NotNull(snapshot);
        Assert.Equal("Add a login page", snapshot.Title);
        Assert.Equal("Use OAuth.\r\nNo passwords.", snapshot.Body);
        Assert.Equal(IssueContent.Hash("Add a login page", "Use OAuth.\r\nNo passwords."), snapshot.Hash);
        Assert.Equal(1, snapshot.SignalSequence);
        Assert.Equal(_time.Now, snapshot.TakenAt);
    }

    [Fact]
    public async Task An_issue_with_no_body_has_an_empty_one()
    {
        await AppendAndTickAsync(LoopStores.Labeled("issue-event:1", body: null));

        var snapshot = await _stores.FindIssueSnapshotAsync(Assert.Single(_stores.Tasks).Id, Token);

        Assert.Equal(string.Empty, snapshot?.Body);
        Assert.Equal(IssueContent.Hash("Add a login page", string.Empty), snapshot?.Hash);
    }

    [Fact]
    public async Task The_branch_name_is_derived_once_from_the_issue_number_and_the_title()
    {
        await AppendAndTickAsync(LoopStores.Labeled("issue-event:1", issue: 12, title: "Add a login page!"));

        Assert.Equal("hephaestus/12-add-a-login-page", Assert.Single(_stores.Tasks).Branch);
    }

    [Fact]
    public async Task A_title_that_yields_no_slug_gives_the_branch_with_the_number_alone()
    {
        await AppendAndTickAsync(LoopStores.Labeled("issue-event:1", issue: 12, title: "!!!"));

        Assert.Equal("hephaestus/12", Assert.Single(_stores.Tasks).Branch);
    }

    [Fact]
    public async Task The_Marker_Label_is_the_same_label_whatever_its_case()
    {
        await AppendAndTickAsync(LoopStores.Labeled("issue-event:1", label: "Hephaestus"));

        Assert.Single(_stores.Tasks);
    }

    [Fact]
    public async Task A_user_who_is_not_trusted_creates_no_Task_and_the_Signal_is_rejected_with_their_id()
    {
        var report = await AppendAndTickAsync(LoopStores.Labeled("issue-event:1", actor: 500, login: "mallory"));

        Assert.Empty(_stores.Tasks);
        var stored = Assert.Single(_stores.Signals);
        Assert.Equal((SignalStatus.Rejected, SignalRejections.ActorNotTrusted), (stored.Status, stored.Reason));
        Assert.Equal(500, stored.Signal.ActorId);
        AssertTick(report, 0, 1, 0, "Signal 1 of actor 500 rejected: actor-not-trusted");
    }

    [Fact]
    public async Task A_login_that_belongs_to_a_Trusted_User_is_not_enough_the_numeric_id_decides()
    {
        await AppendAndTickAsync(LoopStores.Labeled("issue-event:1", actor: 500, login: "alice"));

        Assert.Empty(_stores.Tasks);
        Assert.Equal(SignalStatus.Rejected, Assert.Single(_stores.Signals).Status);
    }

    [Fact]
    public async Task An_event_that_names_no_actor_is_rejected()
    {
        var report = await AppendAndTickAsync(LoopStores.Labeled("issue-event:1", actor: null, login: null));

        Assert.Empty(_stores.Tasks);
        Assert.Equal(SignalRejections.ActorUnknown, Assert.Single(_stores.Signals).Reason);
        Assert.Equal(["Signal 1 of actor none rejected: actor-unknown"], report.Rejections);
    }

    [Fact]
    public async Task An_event_whose_actor_is_Hephaestus_own_identity_creates_and_changes_no_Task_even_if_its_id_were_trusted()
    {
        _stores.AppSlug = "hephaestus-bot";
        _stores.TrustedUsers.Add(new TrustedUser(900, "hephaestus-bot[bot]", DateTimeOffset.UnixEpoch));

        await AppendAndTickAsync(LoopStores.Labeled("issue-event:1", actor: 900, login: "hephaestus-bot[bot]"));

        Assert.Empty(_stores.Tasks);
        Assert.Equal(SignalRejections.OwnIdentity, Assert.Single(_stores.Signals).Reason);
    }

    [Fact]
    public async Task Without_a_stored_slug_an_untrusted_bot_is_still_not_trusted()
    {
        await AppendAndTickAsync(LoopStores.Labeled("issue-event:1", actor: 900, login: "hephaestus-bot[bot]"));

        Assert.Empty(_stores.Tasks);
        Assert.Equal(SignalRejections.ActorNotTrusted, Assert.Single(_stores.Signals).Reason);
    }

    [Fact]
    public async Task Intent_is_read_from_events_only_so_with_no_labelled_event_behind_it_no_Task_exists()
    {
        var comment = new Signal(Signal.GitHubSource, "comment:5:created", SignalKind.CommentCreated, LoopStores.RepositoryId, 7, 29, "alice", null, 5, 29, _time.Now);
        var reopened = new Signal(Signal.GitHubSource, "issue-event:2", SignalKind.Reopened, LoopStores.RepositoryId, 7, 29, "alice", null, null, null, _time.Now);
        var closed = new Signal(Signal.GitHubSource, "issue-event:3", SignalKind.Closed, LoopStores.RepositoryId, 7, 29, "alice", null, null, null, _time.Now);
        var unlabeled = new Signal(Signal.GitHubSource, "issue-event:4", SignalKind.Unlabeled, LoopStores.RepositoryId, 7, 29, "alice", "hephaestus", null, null, _time.Now);

        var report = await AppendAndTickAsync(comment, reopened, closed, unlabeled);

        Assert.Empty(_stores.Tasks);
        AssertTick(report, 4, 0, 0);
        Assert.All(_stores.Signals, stored => Assert.Equal(SignalStatus.Applied, stored.Status));
    }

    [Fact]
    public async Task A_label_other_than_the_Marker_Label_by_a_Trusted_User_creates_no_Task()
    {
        await AppendAndTickAsync(LoopStores.Labeled("issue-event:1", label: "primary:fast"), LoopStores.Labeled("issue-event:2", label: "hephaestus:queued"), LoopStores.Labeled("issue-event:3", label: "bug"));

        Assert.Empty(_stores.Tasks);
        Assert.All(_stores.Signals, stored => Assert.Equal(SignalStatus.Applied, stored.Status));
    }

    [Fact]
    public async Task A_Marker_Label_event_of_a_repository_that_is_not_a_registered_Project_is_rejected()
    {
        await AppendAndTickAsync(LoopStores.Labeled("issue-event:1", repository: 999));

        Assert.Empty(_stores.Tasks);
        Assert.Equal(SignalRejections.ProjectNotRegistered, Assert.Single(_stores.Signals).Reason);
    }

    [Fact]
    public async Task A_removed_Project_is_not_registered_any_more()
    {
        _stores.Projects[0] = _stores.Projects[0] with { RemovedAt = _time.Now };

        await AppendAndTickAsync(LoopStores.Labeled("issue-event:1"));

        Assert.Empty(_stores.Tasks);
        Assert.Equal(SignalRejections.ProjectNotRegistered, Assert.Single(_stores.Signals).Reason);
    }

    [Fact]
    public async Task A_Signal_with_no_issue_title_has_nothing_the_event_could_vouch_for_and_is_rejected()
    {
        await AppendAndTickAsync(LoopStores.Labeled("issue-event:1", title: null));

        Assert.Empty(_stores.Tasks);
        Assert.Equal(SignalRejections.SnapshotMissing, Assert.Single(_stores.Signals).Reason);
    }

    [Fact]
    public async Task A_second_Marker_Label_event_for_an_issue_that_is_a_Task_creates_no_second_Task()
    {
        await AppendAndTickAsync(LoopStores.Labeled("issue-event:1"));
        var first = Assert.Single(_stores.Tasks);

        await AppendAndTickAsync(LoopStores.Labeled("issue-event:2", title: "A different title"));

        Assert.Equal([first], _stores.Tasks);
        Assert.Equal(first.Id, _stores.Signals[1].TaskId);
        Assert.Equal(SignalStatus.Applied, _stores.Signals[1].Status);
        Assert.Equal("Add a login page", (await _stores.FindIssueSnapshotAsync(first.Id, Token))?.Title);
    }

    [Fact]
    public async Task Signals_are_applied_in_inbox_order_so_the_first_actor_to_label_decides_and_the_later_one_changes_nothing()
    {
        var report = await AppendAndTickAsync(
            LoopStores.Labeled("issue-event:1", actor: 500, login: "mallory", title: "Mallory's title"),
            LoopStores.Labeled("issue-event:2", title: "Alice's title"),
            LoopStores.Labeled("issue-event:3", actor: 500, login: "mallory", title: "Mallory again"));

        var task = Assert.Single(_stores.Tasks);
        Assert.Equal("hephaestus/7-alice-s-title", task.Branch);
        Assert.Equal([SignalStatus.Rejected, SignalStatus.Applied, SignalStatus.Rejected], _stores.Signals.Select(s => s.Status));
        AssertTick(report, 1, 2, 1, "Signal 1 of actor 500 rejected: actor-not-trusted", "Signal 3 of actor 500 rejected: actor-not-trusted");
    }

    [Fact]
    public async Task Each_issue_of_a_Project_is_its_own_Task()
    {
        await AppendAndTickAsync(LoopStores.Labeled("issue-event:1", issue: 3), LoopStores.Labeled("issue-event:2", issue: 4));

        Assert.Equal([3, 4], _stores.Tasks.Select(t => t.IssueNumber));
    }

    [Fact]
    public async Task Trust_is_read_again_so_a_user_removed_between_ticks_is_not_trusted_for_the_next_Signal()
    {
        await AppendAndTickAsync(LoopStores.Labeled("issue-event:1", issue: 3));
        _stores.TrustedUsers.Clear();

        await AppendAndTickAsync(LoopStores.Labeled("issue-event:2", issue: 4));

        Assert.Equal([3], _stores.Tasks.Select(t => t.IssueNumber));
    }

    [Fact]
    public async Task A_tick_with_nothing_pending_does_nothing_but_beat()
    {
        var heartbeat = new LoopHeartbeat(_time);
        var loop = new OrchestratorLoop(_stores, _stores, _stores, _stores.Github, _time, heartbeat);

        var report = await loop.TickAsync(Token);

        AssertTick(report, 0, 0, 0);
        Assert.True(heartbeat.Check().Alive);
        Assert.Empty(_stores.Applications);
    }

    [Fact]
    public async Task A_Signal_that_is_no_longer_pending_when_it_is_applied_is_left_alone()
    {
        await _stores.AppendAsync([LoopStores.Labeled("issue-event:1")], Token);
        _stores.BeforeApply = sequence => _stores.Signals[(int)sequence - 1] = _stores.Signals[(int)sequence - 1] with { Status = SignalStatus.Applied };

        var report = await Loop().TickAsync(Token);

        Assert.Empty(_stores.Tasks);
        AssertTick(report, 0, 0, 0);
    }

    [Fact]
    public async Task A_Signal_appended_before_a_restart_and_not_applied_is_applied_by_the_first_tick()
    {
        await _stores.AppendAsync([LoopStores.Labeled("issue-event:1")], Token);

        // A new loop object, as after a restart: nothing is remembered but what the stores hold.
        var report = await Loop().TickAsync(Token);

        Assert.Equal(1, report.TasksCreated);
        Assert.Single(_stores.Tasks);
    }

    [Fact]
    public async Task A_failing_application_stops_the_tick_and_the_same_Signal_is_tried_first_on_the_next_one()
    {
        await _stores.AppendAsync([LoopStores.Labeled("issue-event:1", issue: 3), LoopStores.Labeled("issue-event:2", issue: 4)], Token);
        _stores.Failures[1] = 1;
        var loop = Loop();

        var first = await loop.TickAsync(Token);
        var second = await loop.TickAsync(Token);

        Assert.IsType<InvalidOperationException>(first.Failure);
        Assert.Equal(0, first.Applied);
        Assert.Null(second.Failure);
        Assert.Equal([3, 4], _stores.Tasks.Select(t => t.IssueNumber));
    }

    [Fact]
    public async Task A_Signal_that_fails_again_and_again_for_longer_than_the_window_is_set_aside_so_that_the_Signals_behind_it_are_not_held_back()
    {
        await _stores.AppendAsync([LoopStores.Labeled("issue-event:1", issue: 3), LoopStores.Labeled("issue-event:2", issue: 4)], Token);
        _stores.Failures[1] = OrchestratorLoop.MaximumApplyFailures;
        var loop = Loop();

        TickReport last = null!;
        for (var tick = 0; tick < OrchestratorLoop.MaximumApplyFailures; tick++)
        {
            last = await loop.TickAsync(Token);
            Assert.NotNull(last.Failure);
            _time.Advance(OrchestratorLoop.ApplyFailureWindow);
        }

        Assert.Equal((SignalStatus.Rejected, SignalRejections.ApplyFailed), (_stores.Signals[0].Status, _stores.Signals[0].Reason));
        Assert.Equal(["Signal 1 of actor 29 rejected: apply-failed"], last.Rejections);
        Assert.Equal(1, last.Rejected);
        var report = await loop.TickAsync(Token);
        Assert.Null(report.Failure);
        Assert.Equal([4], _stores.Tasks.Select(t => t.IssueNumber));
    }

    [Fact]
    public async Task A_database_that_is_unavailable_for_a_short_while_costs_nobody_their_opt_in()
    {
        await _stores.AppendAsync([LoopStores.Labeled("issue-event:1")], Token);
        _stores.Failures[1] = 20;
        var loop = Loop();

        for (var tick = 0; tick < 20; tick++)
        {
            Assert.NotNull((await loop.TickAsync(Token)).Failure);
            _time.Advance(OrchestratorLoop.TickInterval);
        }

        Assert.Equal(SignalStatus.Pending, _stores.Signals[0].Status);
        var report = await loop.TickAsync(Token);
        Assert.Null(report.Failure);
        Assert.Single(_stores.Tasks);
    }

    [Fact]
    public async Task When_even_setting_the_Signal_aside_fails_it_stays_pending_and_is_tried_on_the_next_tick()
    {
        await _stores.AppendAsync([LoopStores.Labeled("issue-event:1")], Token);
        _stores.Failures[1] = 100;
        var loop = Loop();

        for (var tick = 0; tick < OrchestratorLoop.MaximumApplyFailures + 1; tick++)
        {
            Assert.NotNull((await loop.TickAsync(Token)).Failure);
            _time.Advance(OrchestratorLoop.ApplyFailureWindow);
        }

        Assert.Equal(SignalStatus.Pending, _stores.Signals[0].Status);
    }

    [Fact]
    public async Task When_the_Signal_is_gone_by_the_time_it_is_set_aside_nothing_is_reported_as_rejected()
    {
        await _stores.AppendAsync([LoopStores.Labeled("issue-event:1")], Token);
        _stores.Failures[1] = OrchestratorLoop.MaximumApplyFailures;
        _stores.BeforeApply = sequence => _stores.Signals[(int)sequence - 1] = _stores.Signals[(int)sequence - 1] with { Status = SignalStatus.Applied };
        var loop = Loop();

        TickReport last = null!;
        for (var tick = 0; tick < OrchestratorLoop.MaximumApplyFailures; tick++)
        {
            last = await loop.TickAsync(Token);
            _time.Advance(OrchestratorLoop.ApplyFailureWindow);
        }

        Assert.Equal((0, 0), (last.Rejected, last.Rejections.Count));
        Assert.NotNull(last.Failure);
    }

    [Fact]
    public async Task A_failure_in_a_later_batch_keeps_what_the_earlier_batch_did()
    {
        var signals = Enumerable.Range(1, OrchestratorLoop.BatchSize + 1).Select(i => LoopStores.Labeled($"issue-event:{i}", issue: i)).ToArray();
        await _stores.AppendAsync(signals, Token);
        _stores.FailToReadAfter = 1;
        _stores.FailToRead = new InvalidOperationException("The inbox went away.");

        var report = await Loop().TickAsync(Token);

        Assert.Equal((OrchestratorLoop.BatchSize, OrchestratorLoop.BatchSize), (report.Applied, report.TasksCreated));
        Assert.Equal("The inbox went away.", report.Failure?.Message);
    }

    [Fact]
    public async Task A_tick_that_failed_does_not_beat_so_a_loop_that_fails_every_time_is_not_alive()
    {
        var heartbeat = new LoopHeartbeat(_time);
        var loop = new OrchestratorLoop(_stores, _stores, _stores, _stores.Github, _time, heartbeat);
        _stores.FailToRead = new InvalidOperationException("The inbox is not there.");

        await loop.TickAsync(Token);

        Assert.Equal(new LoopHealth(false, null), heartbeat.Check());
    }

    [Fact]
    public async Task A_Signal_that_was_applied_beats_even_when_a_later_one_fails()
    {
        var heartbeat = new LoopHeartbeat(_time);
        var loop = new OrchestratorLoop(_stores, _stores, _stores, _stores.Github, _time, heartbeat);
        await _stores.AppendAsync([LoopStores.Labeled("issue-event:1", issue: 3), LoopStores.Labeled("issue-event:2", issue: 4)], Token);
        _stores.Failures[2] = 1;

        var report = await loop.TickAsync(Token);

        Assert.NotNull(report.Failure);
        Assert.Equal(1, report.Applied);
        Assert.True(heartbeat.Check().Alive);
    }

    [Fact]
    public async Task A_report_that_throws_does_not_end_the_loop()
    {
        await _stores.AppendAsync([LoopStores.Labeled("issue-event:1")], Token);
        var waits = new List<TimeSpan>();
        using var stop = new CancellationTokenSource();
        var loop = Loop(waits, onDelay: () =>
        {
            if (waits.Count == 2)
            {
                stop.Cancel();
            }
        });
        var calls = 0;

        await loop.RunAsync(_ => { calls++; throw new InvalidOperationException("the log is gone"); }, stop.Token);

        Assert.Equal(2, calls);
        Assert.Single(_stores.Tasks);
    }

    [Fact]
    public async Task A_cancelled_tick_is_not_a_failure_of_a_Signal()
    {
        await _stores.AppendAsync([LoopStores.Labeled("issue-event:1")], Token);
        _stores.BeforeApply = _ => throw new OperationCanceledException();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Loop().TickAsync(cancelled.Token));
        Assert.Equal(SignalStatus.Pending, _stores.Signals[0].Status);
    }

    [Fact]
    public async Task A_tick_applies_at_most_the_cap_and_the_rest_is_left_for_the_next_one()
    {
        var signals = Enumerable.Range(1, OrchestratorLoop.MaximumSignalsPerTick + 20).Select(i => LoopStores.Labeled($"issue-event:{i}", issue: i)).ToArray();
        await _stores.AppendAsync(signals, Token);
        var loop = Loop();

        var first = await loop.TickAsync(Token);
        var second = await loop.TickAsync(Token);

        Assert.Equal(OrchestratorLoop.MaximumSignalsPerTick, first.Applied);
        Assert.Equal(20, second.Applied);
        Assert.Equal(signals.Length, _stores.Tasks.Count);
    }

    [Fact]
    public async Task Running_ticks_waits_between_ticks_and_ends_when_cancelled()
    {
        await _stores.AppendAsync([LoopStores.Labeled("issue-event:1")], Token);
        var waits = new List<TimeSpan>();
        var reports = new List<TickReport>();
        using var stop = new CancellationTokenSource();
        var loop = Loop(waits, onDelay: () =>
        {
            if (waits.Count == 2)
            {
                stop.Cancel();
            }
        });

        await loop.RunAsync(reports.Add, stop.Token);

        Assert.Equal([OrchestratorLoop.TickInterval, OrchestratorLoop.TickInterval], waits);
        Assert.Equal(2, reports.Count);
        Assert.Equal(1, reports[0].TasksCreated);
        Assert.Single(_stores.Tasks);
    }

    [Fact]
    public async Task Running_does_not_wait_after_a_tick_that_stopped_at_the_cap_because_more_is_pending()
    {
        var signals = Enumerable.Range(1, OrchestratorLoop.MaximumSignalsPerTick + 1).Select(i => LoopStores.Labeled($"issue-event:{i}", issue: i)).ToArray();
        await _stores.AppendAsync(signals, Token);
        var waits = new List<TimeSpan>();
        using var stop = new CancellationTokenSource();
        var loop = Loop(waits, onDelay: stop.Cancel);

        await loop.RunAsync(_ => { }, stop.Token);

        Assert.Single(waits);
        Assert.Equal(signals.Length, _stores.Tasks.Count);
    }

    [Fact]
    public async Task Running_ends_without_a_report_when_it_is_cancelled_in_the_middle_of_a_tick()
    {
        await _stores.AppendAsync([LoopStores.Labeled("issue-event:1")], Token);
        using var stop = new CancellationTokenSource();
        _stores.BeforeApply = _ =>
        {
            stop.Cancel();
            throw new OperationCanceledException(stop.Token);
        };
        var reports = new List<TickReport>();

        await Loop().RunAsync(reports.Add, stop.Token);

        Assert.Empty(reports);
        Assert.Equal(SignalStatus.Pending, _stores.Signals[0].Status);
    }

    [Fact]
    public async Task Running_reports_a_tick_that_failed_and_goes_on()
    {
        _stores.FailToRead = new InvalidOperationException("The inbox is not there.");
        var reports = new List<TickReport>();
        using var stop = new CancellationTokenSource();
        var loop = Loop(onDelay: () =>
        {
            if (reports.Count == 2)
            {
                stop.Cancel();
            }
        });

        await loop.RunAsync(reports.Add, stop.Token);

        Assert.Equal(2, reports.Count);
        Assert.All(reports, report => Assert.Equal("The inbox is not there.", report.Failure?.Message));
    }

    [Fact]
    public async Task Running_ends_at_once_when_it_is_cancelled_during_a_tick()
    {
        _stores.FailToRead = new OperationCanceledException();
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();
        var reports = new List<TickReport>();

        await Loop().RunAsync(reports.Add, stop.Token);

        Assert.Empty(reports);
    }

    [Fact]
    public async Task The_default_wait_between_ticks_is_the_interval_on_the_loops_clock()
    {
        using var stop = new CancellationTokenSource();
        var loop = new OrchestratorLoop(_stores, _stores, _stores, _stores.Github, TimeProvider.System, new LoopHeartbeat(TimeProvider.System));
        var reports = new List<TickReport>();

        var run = loop.RunAsync(report => { reports.Add(report); stop.Cancel(); }, stop.Token);
        await run.WaitAsync(TimeSpan.FromSeconds(10), Token);

        Assert.Single(reports);
    }
}
