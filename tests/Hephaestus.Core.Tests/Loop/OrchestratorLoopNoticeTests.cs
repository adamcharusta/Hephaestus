using Hephaestus.Core.Effects;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Loop;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>
/// Story 2.5, AD-5, AD-15, NFR-9, NFR-10: a label that someone who is not a Trusted User applied is said on the issue to have been ignored, once for
/// each user, kind of action, and issue; a comment or a command from such a user is recorded and gets no reply; and the comment is journaled,
/// found by its marker after a crash, retried within a bound, and in the language of the text on GitHub.
/// </summary>
public class OrchestratorLoopNoticeTests
{
    private const long Repository = LoopStores.RepositoryId;
    private const string Ignored = "**Ignored.** `bob` is not a Trusted User, so the `hephaestus` label has no effect.";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly LoopStores _stores = new();
    private readonly MovingTime _time = new();

    public OrchestratorLoopNoticeTests()
    {
        _stores.TimeProvider = _time;
        _stores.AppSlug = "hephaestus-test";
    }

    private OrchestratorLoop NewLoop() => new(_stores, _stores, _stores, _stores.Github, _stores.Notifier, _time, new LoopHeartbeat(_time));

    private async Task<TickReport> TickWithAsync(OrchestratorLoop loop, params Signal[] signals)
    {
        await _stores.AppendAsync(signals, Token);
        return await loop.TickAsync(Token);
    }

    private Task<TickReport> TickWithAsync(params Signal[] signals) => TickWithAsync(NewLoop(), signals);

    private static Signal ByBob(string id, int issue = 7, string label = "hephaestus", long? actor = 1001, string? login = "bob") =>
        LoopStores.Labeled(id, actor, login, label, issue);

    private static Signal CommentBy(string id, long? actor, string? login, SignalKind kind = SignalKind.CommentCreated, long repository = Repository) =>
        new(Signal.GitHubSource, id, kind, repository, 7, actor, login, null, 5, actor, new DateTimeOffset(2026, 11, 27, 11, 30, 0, TimeSpan.Zero));

    private IEnumerable<JournalEntry> Comments(JournalEntryKind? entry = null) =>
        _stores.Journal.Where(e => e.Effect.Kind == EffectKind.Comment && (entry is null || e.Entry == entry));

    [Fact]
    public async Task A_label_applied_by_a_user_who_is_not_a_Trusted_User_is_ignored_and_a_comment_on_the_issue_says_why_and_no_Task_is_made()
    {
        var report = await TickWithAsync(ByBob("issue-event:1"));

        Assert.Empty(_stores.Tasks);
        var signal = Assert.Single(_stores.Signals);
        Assert.Equal((SignalStatus.Rejected, SignalRejections.ActorNotTrusted), (signal.Status, signal.Reason));
        var comment = Assert.Single(_stores.Github.Comments);
        Assert.Equal((Repository, 7, Ignored), (comment.RepositoryId, comment.IssueNumber, comment.Text));
        Assert.Equal(1, report.EffectsEnded);
        Assert.Empty(report.Problems ?? []);
    }

    [Fact]
    public async Task The_comment_is_journaled_with_the_Signal_that_caused_it_under_a_key_of_the_Project_and_its_result_is_recorded_after_it_was_posted()
    {
        await TickWithAsync(ByBob("issue-event:1"));

        var application = Assert.Single(_stores.Applications);
        Assert.Equal(SignalStatus.Rejected, application.Status);
        var effect = Assert.Single(application.NewEffects!);
        Assert.Equal(((Guid?)null, "g1", EffectKind.Comment, (long?)Repository, (int?)7), (effect.TaskId, effect.Cause, effect.Kind, effect.RepositoryId, effect.IssueNumber));
        var intent = Assert.Single(Comments(JournalEntryKind.Intent));
        Assert.Equal($"project:{Repository}:g1:comment:0", intent.Effect.Key);
        Assert.Equal(intent.Effect.Key, Assert.Single(_stores.Github.Comments).Key);
        var result = Assert.Single(Comments(JournalEntryKind.Result));
        Assert.Equal((intent.Effect.Key, EffectResult.Applied, null), (result.Effect.Key, result.Result, result.Reason));
    }

    [Fact]
    public async Task The_same_user_repeating_the_same_kind_of_action_on_the_same_issue_gets_one_comment_and_every_attempt_is_still_recorded()
    {
        var loop = NewLoop();
        await TickWithAsync(loop, ByBob("issue-event:1"));

        await TickWithAsync(loop, ByBob("issue-event:2"), ByBob("issue-event:3", label: "Hephaestus"));
        await TickWithAsync(loop, ByBob("issue-event:4"));

        Assert.Single(_stores.Github.Comments);
        Assert.Single(Comments(JournalEntryKind.Intent));
        Assert.All(_stores.Signals, stored => Assert.Equal((SignalStatus.Rejected, SignalRejections.ActorNotTrusted), (stored.Status, stored.Reason)));
        Assert.Equal(4, _stores.Signals.Count);
    }

    [Fact]
    public async Task Another_user_or_another_issue_gets_a_comment_of_its_own()
    {
        await TickWithAsync(ByBob("issue-event:1"), ByBob("issue-event:2", actor: 1002, login: "carol"), ByBob("issue-event:3", issue: 8));

        Assert.Equal(3, _stores.Github.Comments.Count);
        Assert.Equal([7, 7, 8], _stores.Github.Comments.Select(c => c.IssueNumber).Order());
        Assert.Contains(_stores.Github.Comments, c => c.Text.Contains("`carol`", StringComparison.Ordinal));
        Assert.Equal(3, _stores.Github.Comments.Select(c => c.Key).Distinct().Count());
    }

    [Fact]
    public async Task A_user_who_is_not_a_Trusted_User_applying_the_label_to_a_Tasks_issue_changes_nothing_and_the_comment_belongs_to_the_Task()
    {
        var loop = NewLoop();
        await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));
        var task = Assert.Single(_stores.Tasks);

        await TickWithAsync(loop, ByBob("issue-event:2"));

        Assert.Equal(task, Assert.Single(_stores.Tasks));
        var comment = Assert.Single(_stores.Github.Comments);
        Assert.Equal($"{task.Id}:g2:comment:0", comment.Key);
        Assert.Equal(Ignored, comment.Text);
        Assert.Equal(task.Id, Comments(JournalEntryKind.Intent).Single().Effect.TaskId);
    }

    [Fact]
    public async Task Nothing_is_said_when_Hephaestus_itself_acted_when_nobody_is_named_when_the_repository_is_no_Project_or_the_login_is_no_login()
    {
        await TickWithAsync(
            ByBob("issue-event:1", actor: 900, login: "hephaestus-test[bot]"),
            ByBob("issue-event:2", actor: null, login: null),
            ByBob("issue-event:3", actor: 1003, login: "not a login"),
            LoopStores.Labeled("issue-event:4", actor: 1001, login: "bob", repository: 999),
            ByBob("issue-event:5", label: "bug"));

        Assert.Empty(_stores.Github.Comments);
        Assert.Empty(_stores.Journal);
        Assert.Equal(
            [SignalRejections.OwnIdentity, SignalRejections.ActorUnknown, SignalRejections.ActorNotTrusted, SignalRejections.ProjectNotRegistered, null],
            _stores.Signals.Select(s => s.Reason));
    }

    [Fact]
    public async Task A_comment_or_a_command_from_a_user_who_is_not_a_Trusted_User_is_recorded_as_rejected_with_the_users_id_and_gets_no_reply()
    {
        var report = await TickWithAsync(CommentBy("comment:5:created", 1001, "bob"), CommentBy("comment:6:created", null, null));

        Assert.Equal([SignalRejections.ActorNotTrusted, SignalRejections.ActorUnknown], _stores.Signals.Select(s => s.Reason));
        Assert.All(_stores.Signals, stored => Assert.Equal(SignalStatus.Rejected, stored.Status));
        Assert.Equal(1001, _stores.Signals[0].Signal.ActorId);
        Assert.Equal("Signal 1 of actor 1001 rejected: actor-not-trusted", report.Rejections[0]);
        Assert.Empty(_stores.Tasks);
        Assert.Empty(_stores.Journal);
        Assert.Empty(_stores.Github.Comments);
        Assert.Equal(0, _stores.Github.Reads + _stores.Github.Finds);
    }

    [Fact]
    public async Task A_comment_from_a_Trusted_User_Hephaestus_itself_or_on_no_Project_and_an_edit_are_applied_with_no_change()
    {
        await TickWithAsync(
            CommentBy("comment:5:created", 29, "alice"),
            CommentBy("comment:6:created", 900, "hephaestus-test[bot]"),
            CommentBy("comment:7:created", 1001, "bob", repository: 999),
            CommentBy("comment:5:edited:1", null, null, SignalKind.CommentEdited));

        Assert.All(_stores.Signals, stored => Assert.Equal((SignalStatus.Applied, null), (stored.Status, stored.Reason)));
        Assert.Empty(_stores.Tasks);
        Assert.Empty(_stores.Journal);
    }

    [Fact]
    public async Task A_comment_posted_before_a_crash_and_not_recorded_is_found_by_its_marker_and_is_not_posted_again()
    {
        var loop = NewLoop();
        _stores.FailToRecord = new InvalidOperationException("The database is not there.");
        var failed = await TickWithAsync(loop, ByBob("issue-event:1"));
        Assert.NotNull(failed.Failure);
        Assert.Single(_stores.Github.Comments);
        Assert.Empty(Comments(JournalEntryKind.Result));

        _stores.FailToRecord = null;
        var restarted = await NewLoop().TickAsync(Token);

        Assert.Null(restarted.Failure);
        Assert.Single(_stores.Github.Comments);
        Assert.Equal(EffectResult.AlreadyInPlace, Assert.Single(Comments(JournalEntryKind.Result)).Result);
    }

    [Fact]
    public async Task A_comment_that_was_not_posted_before_the_crash_is_posted_once_by_the_first_tick_after_it()
    {
        var loop = NewLoop();
        _stores.Github.Throws = new HttpRequestException("The network is gone.");
        await TickWithAsync(loop, ByBob("issue-event:1"));
        Assert.Empty(_stores.Github.Comments);

        _stores.Github.Throws = null;
        _time.Advance(TimeSpan.FromMinutes(1));
        await NewLoop().TickAsync(Token);
        await NewLoop().TickAsync(Token);

        Assert.Single(_stores.Github.Comments);
        Assert.Equal(EffectResult.Applied, Assert.Single(Comments(JournalEntryKind.Result)).Result);
    }

    [Fact]
    public async Task An_unreachable_GitHub_is_retried_within_the_bound_and_at_the_bound_the_comment_is_given_up_and_only_the_log_is_told_because_no_Task_waits()
    {
        _stores.Settings["effectRetries"] = "2";
        _stores.Github.CommentAnswer = GitHubCommentResult.Failed(GitHubIssueFailure.Unreachable);
        var loop = NewLoop();
        var report = await TickWithAsync(loop, ByBob("issue-event:1"));
        Assert.Equal(1, report.EffectsRetried);

        _time.Advance(EffectRetries.DelayAfter(1));
        await loop.TickAsync(Token);
        _time.Advance(EffectRetries.DelayAfter(2));
        var last = await loop.TickAsync(Token);

        var ended = Assert.Single(Comments(JournalEntryKind.Result));
        Assert.Equal((EffectResult.GaveUp, "unreachable", 3), (ended.Result, ended.Reason, ended.Failures));
        var problem = Assert.Single(last.Problems!);
        Assert.Equal((null, ended.Effect.Key, EffectResult.GaveUp, false), (problem.TaskId, problem.Key, problem.Result, problem.MovedToWaitingForOwner));
        Assert.Equal(2, Comments(JournalEntryKind.Retry).Count());
        Assert.Empty(await _stores.ListUnfinishedEffectsAsync(Token));
    }

    [Fact]
    public async Task A_post_whose_answer_is_lost_is_tried_again_at_the_recorded_time_finds_the_comment_it_made_and_posts_no_second()
    {
        _stores.Github.PostAnswers.Enqueue(GitHubCommentResult.Failed(GitHubIssueFailure.Unreachable));
        var loop = NewLoop();
        await TickWithAsync(loop, ByBob("issue-event:1"));
        var retry = Assert.Single(Comments(JournalEntryKind.Retry));
        Assert.Equal(("unreachable", 1, _time.Now + EffectRetries.FirstDelay), (retry.Reason, retry.Failures, retry.NextAttemptAt));

        _time.Advance(EffectRetries.FirstDelay);
        await loop.TickAsync(Token);

        Assert.Single(_stores.Github.Comments);
        Assert.Equal(EffectResult.AlreadyInPlace, Assert.Single(Comments(JournalEntryKind.Result)).Result);
    }

    [Fact]
    public async Task A_comment_on_a_Tasks_issue_that_GitHub_refuses_for_good_ends_with_the_refusal_and_the_Task_keeps_its_state_because_the_comment_is_not_its_own_effect()
    {
        var loop = NewLoop();
        await TickWithAsync(loop, LoopStores.Labeled("issue-event:1"));
        _stores.Github.CommentAnswer = GitHubCommentResult.Failed(GitHubIssueFailure.Rejected);

        var report = await TickWithAsync(loop, ByBob("issue-event:2"));

        var refusal = Assert.Single(Comments(JournalEntryKind.Result));
        Assert.Equal((EffectResult.Refused, "rejected"), (refusal.Result, refusal.Reason));
        var task = Assert.Single(_stores.Tasks);
        Assert.Equal(TaskState.Queued, task.State);
        var problem = Assert.Single(report.Problems!);
        Assert.Equal((task.Id, false, (long?)Repository, (int?)7), (problem.TaskId, problem.MovedToWaitingForOwner, problem.RepositoryId, problem.IssueNumber));
        Assert.DoesNotContain(_stores.Journal, e => e.Effect.Kind == EffectKind.Label && e.Effect.Detail == "waiting-for-owner");
    }

    [Fact]
    public async Task A_label_effect_that_is_for_no_Task_cannot_be_made_and_is_refused_and_the_effects_behind_it_are_still_made()
    {
        _stores.Journal.Add(new JournalEntry(1, JournalEntryKind.Intent, new Effect("project:101:g1:label:0", null, "g1", EffectKind.Label, "queued", 0, Repository, 7), null, null, 0, null, _time.Now));

        var report = await TickWithAsync(ByBob("issue-event:1"));

        var refusal = Assert.Single(_stores.Journal, e => e.Entry == JournalEntryKind.Result && e.Effect.Kind == EffectKind.Label);
        Assert.Equal((EffectResult.Refused, "unknown-effect"), (refusal.Result, refusal.Reason));
        Assert.Null(report.Failure);
        Assert.Single(_stores.Github.Comments);
    }

    [Fact]
    public async Task A_comment_on_an_issue_that_is_gone_is_refused_and_not_tried_again()
    {
        _stores.Github.Missing.Add((Repository, 7));

        var report = await TickWithAsync(ByBob("issue-event:1"));
        await NewLoop().TickAsync(Token);

        Assert.Equal(("issue-not-found", EffectResult.Refused), (Assert.Single(Comments(JournalEntryKind.Result)).Reason, Assert.Single(Comments(JournalEntryKind.Result)).Result));
        Assert.Single(report.Problems!);
        Assert.Empty(_stores.Github.Comments);
    }

    [Fact]
    public async Task A_rate_limit_is_a_wait_and_not_a_failure_for_a_comment_too()
    {
        _stores.Github.CommentAnswer = GitHubCommentResult.Failed(GitHubIssueFailure.RateLimited, _time.Now.AddMinutes(5));

        await TickWithAsync(ByBob("issue-event:1"));

        var retry = Assert.Single(Comments(JournalEntryKind.Retry));
        Assert.Equal(("rate-limited", 0, _time.Now.AddMinutes(5)), (retry.Reason, retry.Failures, retry.NextAttemptAt));
    }

    [Fact]
    public async Task A_gateway_that_throws_is_an_attempt_that_failed_and_the_exception_is_reported_with_the_tick()
    {
        _stores.Github.Throws = new HttpRequestException("The network is gone.");

        var report = await TickWithAsync(ByBob("issue-event:1"));

        Assert.IsType<HttpRequestException>(report.Failure);
        Assert.Equal(("unreachable", 1), (Assert.Single(Comments(JournalEntryKind.Retry)).Reason, Assert.Single(Comments(JournalEntryKind.Retry)).Failures));
    }

    [Theory]
    [InlineData("pl", "**Zignorowano.** `bob` nie jest zaufanym użytkownikiem (Trusted User), więc etykieta `hephaestus` nie ma żadnego skutku.")]
    [InlineData("en", Ignored)]
    [InlineData("de", Ignored)]
    public async Task The_sentence_is_in_the_language_of_the_installation_and_the_label_is_never_translated(string language, string expected)
    {
        _stores.Settings["language"] = language;

        await TickWithAsync(ByBob("issue-event:1"));

        Assert.Equal(expected, Assert.Single(_stores.Github.Comments).Text);
    }

    [Fact]
    public async Task With_no_language_stored_the_one_the_environment_names_is_used()
    {
        var loop = new OrchestratorLoop(_stores, _stores, _stores, _stores.Github, _stores.Notifier, _time, new LoopHeartbeat(_time), fallbackLanguage: Hephaestus.Core.Localization.Language.Polish);

        await TickWithAsync(loop, ByBob("issue-event:1"));

        Assert.StartsWith("**Zignorowano.**", Assert.Single(_stores.Github.Comments).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_intent_this_version_cannot_make_is_refused_and_does_not_stop_the_loop()
    {
        var loop = NewLoop();
        await TickWithAsync(loop, ByBob("issue-event:1"));
        _stores.Github.Comments.Clear();
        _stores.Journal.RemoveAll(e => e.Entry == JournalEntryKind.Result);
        _stores.Journal[0] = _stores.Journal[0] with { Effect = _stores.Journal[0].Effect with { Detail = "something-else|x" } };

        var report = await loop.TickAsync(Token);

        Assert.Equal((EffectResult.Refused, "unknown-effect"), (Assert.Single(Comments(JournalEntryKind.Result)).Result, Assert.Single(Comments(JournalEntryKind.Result)).Reason));
        Assert.Single(report.Problems!);
        Assert.Empty(_stores.Github.Comments);
    }

    [Fact]
    public async Task Recorded_events_of_untrusted_actors_change_no_Task_and_a_renamed_Trusted_User_is_still_recognized_by_numeric_id()
    {
        var loop = NewLoop();

        await TickWithAsync(
            loop,
            ByBob("issue-event:1", issue: 3),
            ByBob("issue-event:2", issue: 4, actor: 1003, login: "mallory"),
            CommentBy("comment:5:created", 1003, "mallory"),
            LoopStores.Labeled("issue-event:3", actor: 29, login: "alice-renamed", issue: 5),
            ByBob("issue-event:4", issue: 5));

        var task = Assert.Single(_stores.Tasks);
        Assert.Equal((5, TaskState.Queued), (task.IssueNumber, task.State));
        Assert.Equal(
            [SignalStatus.Rejected, SignalStatus.Rejected, SignalStatus.Rejected, SignalStatus.Applied, SignalStatus.Rejected],
            _stores.Signals.Select(s => s.Status));
        Assert.Equal(3, _stores.Github.Comments.Count);
        Assert.DoesNotContain(_stores.Github.Comments, c => c.Text.Contains("alice-renamed", StringComparison.Ordinal));
    }
}
