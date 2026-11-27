using Hephaestus.Core.Escalations;
using Hephaestus.Core.Loop;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>
/// Story 2.10, FR-49, AD-1, AD-15, NFR-1: a Trusted User's reply on the issue of a waiting Task is admitted and recorded as the answer, and the Task
/// returns to the Queue; a reply from anyone else, and Hephaestus's own comments, change nothing; a Trusted User's comment on a Task that is not
/// waiting is context only; a command is not an answer.
/// </summary>
public class OrchestratorLoopAnswerTests
{
    private const long Repository = LoopStores.RepositoryId;
    private const int Issue = 7;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly LoopStores _stores = new();
    private readonly MovingTime _time = new();

    public OrchestratorLoopAnswerTests()
    {
        _stores.TimeProvider = _time;
        _stores.AppSlug = "hephaestus-test";
    }

    private OrchestratorLoop NewLoop() =>
        new(_stores, _stores, _stores, _stores.Github, _stores.Notifier, _time, new LoopHeartbeat(_time));

    private async Task<TickReport> TickWithAsync(OrchestratorLoop loop, params Signal[] signals)
    {
        await _stores.AppendAsync(signals, Token);
        return await loop.TickAsync(Token);
    }

    private async Task<(OrchestratorLoop Loop, TaskRecord Task)> TaskInAsync(TaskState state, int issue = Issue)
    {
        var loop = NewLoop();
        await TickWithAsync(loop, LoopStores.Labeled($"issue-event:{issue}", issue: issue));
        var index = _stores.Tasks.FindIndex(t => t.IssueNumber == issue);
        _stores.Tasks[index] = _stores.Tasks[index] with { State = state };
        return (loop, _stores.Tasks[index]);
    }

    /// <summary>A Task that asked a question, so that it waits with an open Escalation; the effects of the question are made.</summary>
    private async Task<(OrchestratorLoop Loop, TaskRecord Task, Escalation Escalation)> WaitingTaskAsync(int issue = Issue)
    {
        var (loop, task) = await TaskInAsync(TaskState.Running, issue);
        await loop.RaiseEscalationAsync(task, EscalationRequest.For("Which database should the login page use?", ["SQLite", "Postgres"]), "s4", Token);
        await loop.TickAsync(Token);
        return (loop, _stores.Tasks.Single(t => t.Id == task.Id), _stores.Escalations.Single(e => e.TaskId == task.Id));
    }

    private string[] Labels(int issue = Issue) => [.. _stores.Github.LabelsOf(Repository, issue).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task A_reply_of_a_Trusted_User_is_admitted_and_recorded_as_the_answer_and_the_Task_returns_to_Queued()
    {
        var (loop, task, escalation) = await WaitingTaskAsync();

        var report = await TickWithAsync(loop, LoopStores.Comment("comment:500:created", body: "Use Postgres."));

        Assert.Equal((1, 0), (report.Applied, report.Rejected));
        Assert.Equal(TaskState.Queued, Assert.Single(_stores.Tasks).State);
        var answer = Assert.Single(_stores.AdmittedComments);
        Assert.Equal((task.Id, 500L, 29L, "alice", "Use Postgres.", escalation.Id), (answer.TaskId, answer.CommentId, answer.AuthorId, answer.AuthorLogin, answer.Body, answer.AnswersEscalationId));
        Assert.Equal(CommentContent.Hash("Use Postgres."), answer.Hash);
        Assert.Equal(_stores.Signals[^1].Sequence, answer.SignalSequence);
        Assert.Equal(_time.Now, answer.AdmittedAt);
        var ended = Assert.Single(_stores.Escalations);
        Assert.Equal((EscalationOutcome.Answered, _time.Now), (ended.Outcome, ended.ClosedAt));
        Assert.Equal([answer], await _stores.ListAdmittedCommentsAsync(task.Id, Token));
    }

    [Fact]
    public async Task The_issue_shows_the_Queue_again_after_the_answer()
    {
        var (loop, _, _) = await WaitingTaskAsync();
        Assert.Equal(["hephaestus", "hephaestus:waiting-for-owner"], Labels());

        await TickWithAsync(loop, LoopStores.Comment("comment:500:created"));
        var report = await loop.TickAsync(Token);

        Assert.Equal(["hephaestus", "hephaestus:queued"], Labels());
        Assert.Null(report.Failure);
    }

    [Fact]
    public async Task A_Task_that_was_answered_is_eligible_for_dispatch_again_and_a_second_reply_is_context_only()
    {
        var (loop, task, _) = await WaitingTaskAsync();
        await TickWithAsync(loop, LoopStores.Comment("comment:500:created", body: "Use Postgres."));
        Assert.Equal([task.Id], DispatchPolicy.Eligible(_stores.Tasks).Select(t => t.Id));

        await TickWithAsync(loop, LoopStores.Comment("comment:501:created", body: "Also add a test.", commentId: 501));

        Assert.Equal(TaskState.Queued, Assert.Single(_stores.Tasks).State);
        Assert.Equal([EscalationOutcome.Answered], _stores.Escalations.Select(e => e.Outcome));
        Assert.Equal([(500L, true), (501L, false)], _stores.AdmittedComments.Select(c => (c.CommentId, c.AnswersEscalationId is not null)));
    }

    [Fact]
    public async Task A_reply_to_an_Escalation_about_an_effect_that_could_not_be_completed_resumes_the_Task_the_same_way()
    {
        var (loop, task) = await TaskInAsync(TaskState.Queued);
        _stores.Github.Answer = Hephaestus.Core.GitHub.GitHubIssueResult.Failed(Hephaestus.Core.GitHub.GitHubIssueFailure.Rejected);
        await TickWithAsync(loop, LoopStores.Labeled("issue-event:9", issue: 9));
        await loop.TickAsync(Token);
        var waiting = _stores.Tasks.Single(t => t.IssueNumber == 9);
        Assert.Equal(TaskState.WaitingForOwner, waiting.State);
        Assert.Equal(EscalationKind.EffectFailed, Assert.Single(_stores.Escalations, e => e.TaskId == waiting.Id).Kind);

        _stores.Github.Answer = null;
        await TickWithAsync(loop, LoopStores.Comment("comment:600:created", issue: 9, commentId: 600, body: "Fixed the permissions."));

        Assert.Equal(TaskState.Queued, _stores.Tasks.Single(t => t.Id == waiting.Id).State);
        Assert.Equal(EscalationOutcome.Answered, Assert.Single(_stores.Escalations, e => e.TaskId == waiting.Id).Outcome);
        Assert.Equal(TaskState.Queued, _stores.Tasks.Single(t => t.Id == task.Id).State);
    }

    [Fact]
    public async Task A_comment_from_someone_who_is_not_a_Trusted_User_is_not_admitted_the_Task_keeps_waiting_and_nothing_is_said()
    {
        var (loop, _, _) = await WaitingTaskAsync();
        var comments = _stores.Github.Comments.Count;

        var report = await TickWithAsync(loop, LoopStores.Comment("comment:500:created", actor: 1001, login: "mallory", body: "Use Postgres."));
        await loop.TickAsync(Token);

        Assert.Equal((0, 1), (report.Applied, report.Rejected));
        var stored = _stores.Signals[^1];
        Assert.Equal((SignalStatus.Rejected, SignalRejections.ActorNotTrusted), (stored.Status, stored.Reason));
        Assert.Contains("actor 1001", Assert.Single(report.Rejections), StringComparison.Ordinal);
        Assert.Equal(TaskState.WaitingForOwner, Assert.Single(_stores.Tasks).State);
        Assert.Empty(_stores.AdmittedComments);
        Assert.Null(Assert.Single(_stores.Escalations).ClosedAt);
        Assert.Equal(comments, _stores.Github.Comments.Count);
        Assert.Equal(["hephaestus", "hephaestus:waiting-for-owner"], Labels());
    }

    [Fact]
    public async Task A_comment_that_names_no_author_is_not_admitted_and_the_Task_keeps_waiting()
    {
        var (loop, _, _) = await WaitingTaskAsync();

        await TickWithAsync(loop, LoopStores.Comment("comment:500:created", actor: null, login: null));

        Assert.Equal(SignalRejections.ActorUnknown, _stores.Signals[^1].Reason);
        Assert.Equal(TaskState.WaitingForOwner, Assert.Single(_stores.Tasks).State);
        Assert.Empty(_stores.AdmittedComments);
    }

    [Fact]
    public async Task A_comment_by_Hephaestus_s_own_identity_is_never_admitted_even_when_its_id_is_a_Trusted_User_s()
    {
        var (loop, _, _) = await WaitingTaskAsync();

        await TickWithAsync(loop, LoopStores.Comment("comment:500:created", actor: 29, login: "hephaestus-test[bot]"));

        Assert.Equal(SignalStatus.Applied, _stores.Signals[^1].Status);
        Assert.Equal(TaskState.WaitingForOwner, Assert.Single(_stores.Tasks).State);
        Assert.Empty(_stores.AdmittedComments);
        Assert.Null(Assert.Single(_stores.Escalations).ClosedAt);
    }

    [Theory]
    [InlineData(TaskState.Queued)]
    [InlineData(TaskState.Running)]
    [InlineData(TaskState.WaitingForCapacity)]
    [InlineData(TaskState.PullRequestOpen)]
    [InlineData(TaskState.Failed)]
    public async Task A_comment_of_a_Trusted_User_on_a_Task_that_is_not_waiting_is_admitted_as_context_and_changes_no_state(TaskState state)
    {
        var (loop, task) = await TaskInAsync(state);
        var journal = _stores.Journal.Count;

        await TickWithAsync(loop, LoopStores.Comment("comment:500:created", body: "Mind the migration."));

        Assert.Equal(state, Assert.Single(_stores.Tasks).State);
        var comment = Assert.Single(_stores.AdmittedComments);
        Assert.Equal((task.Id, "Mind the migration.", null), (comment.TaskId, comment.Body, comment.AnswersEscalationId));
        Assert.Empty(_stores.Escalations);
        Assert.Equal(journal, _stores.Journal.Count);
    }

    [Theory]
    [InlineData("/hephaestus confirm")]
    [InlineData("/hephaestus confirm\nthe rest")]
    [InlineData("  /Hephaestus whatever")]
    [InlineData("/hephaestus")]
    public async Task A_command_is_not_an_answer_and_not_context_and_the_Task_keeps_waiting(string body)
    {
        var (loop, _, _) = await WaitingTaskAsync();

        await TickWithAsync(loop, LoopStores.Comment("comment:500:created", body: body));

        Assert.Equal(SignalStatus.Applied, _stores.Signals[^1].Status);
        Assert.Equal(TaskState.WaitingForOwner, Assert.Single(_stores.Tasks).State);
        Assert.Empty(_stores.AdmittedComments);
        Assert.Null(Assert.Single(_stores.Escalations).ClosedAt);
    }

    [Fact]
    public async Task A_command_on_the_issue_of_a_Task_that_is_not_waiting_is_not_admitted_either()
    {
        var (loop, _) = await TaskInAsync(TaskState.Running);

        await TickWithAsync(loop, LoopStores.Comment("comment:500:created", body: "/hephaestus confirm"));

        Assert.Empty(_stores.AdmittedComments);
        Assert.Equal(TaskState.Running, Assert.Single(_stores.Tasks).State);
    }

    [Fact]
    public async Task A_command_in_a_later_line_is_a_comment_like_any_other_and_answers()
    {
        var (loop, _, _) = await WaitingTaskAsync();

        await TickWithAsync(loop, LoopStores.Comment("comment:500:created", body: "Use Postgres.\n/hephaestus confirm"));

        Assert.Equal(TaskState.Queued, Assert.Single(_stores.Tasks).State);
        Assert.Single(_stores.AdmittedComments);
    }

    [Fact]
    public async Task A_comment_made_before_the_Escalation_was_raised_is_context_and_does_not_answer_it()
    {
        var (loop, _, _) = await WaitingTaskAsync();

        await TickWithAsync(loop, LoopStores.Comment("comment:500:created", at: _time.Now.AddMinutes(-5)));

        Assert.Equal(TaskState.WaitingForOwner, Assert.Single(_stores.Tasks).State);
        Assert.Null(Assert.Single(_stores.AdmittedComments).AnswersEscalationId);
        Assert.Null(Assert.Single(_stores.Escalations).ClosedAt);
    }

    [Fact]
    public async Task A_waiting_Task_with_no_open_Escalation_takes_a_comment_as_context_only()
    {
        var (loop, _) = await TaskInAsync(TaskState.WaitingForOwner);

        await TickWithAsync(loop, LoopStores.Comment("comment:500:created"));

        Assert.Equal(TaskState.WaitingForOwner, Assert.Single(_stores.Tasks).State);
        Assert.Null(Assert.Single(_stores.AdmittedComments).AnswersEscalationId);
    }

    [Fact]
    public async Task A_waiting_Task_whose_Escalation_waits_for_something_else_is_not_resumed_by_a_comment()
    {
        var (loop, _, escalation) = await WaitingTaskAsync();
        _stores.Escalations[0] = escalation with { ResumeCondition = "confirmation" };

        await TickWithAsync(loop, LoopStores.Comment("comment:500:created"));

        Assert.Equal(TaskState.WaitingForOwner, Assert.Single(_stores.Tasks).State);
        Assert.Null(Assert.Single(_stores.AdmittedComments).AnswersEscalationId);
    }

    [Fact]
    public async Task A_comment_with_no_text_on_its_Signal_is_not_admitted()
    {
        var (loop, _, _) = await WaitingTaskAsync();

        await TickWithAsync(loop, LoopStores.Comment("comment:500:created", body: null));

        Assert.Equal(TaskState.WaitingForOwner, Assert.Single(_stores.Tasks).State);
        Assert.Empty(_stores.AdmittedComments);
    }

    [Fact]
    public async Task A_comment_on_an_issue_that_is_no_Task_or_in_no_Project_admits_nothing()
    {
        var (loop, _, _) = await WaitingTaskAsync();

        await TickWithAsync(loop, LoopStores.Comment("comment:500:created", issue: 99), LoopStores.Comment("comment:501:created", commentId: 501, repository: 999));

        Assert.Empty(_stores.AdmittedComments);
        Assert.All(_stores.Signals.TakeLast(2), stored => Assert.Equal(SignalStatus.Applied, stored.Status));
        Assert.Equal(TaskState.WaitingForOwner, Assert.Single(_stores.Tasks).State);
    }

    [Fact]
    public async Task The_answer_is_for_the_Task_of_the_issue_and_another_waiting_Task_keeps_waiting()
    {
        var (loop, first, _) = await WaitingTaskAsync(issue: 3);
        var (_, second, _) = await WaitingTaskAsync(issue: 4);

        await TickWithAsync(loop, LoopStores.Comment("comment:500:created", issue: 4));

        Assert.Equal(TaskState.WaitingForOwner, _stores.Tasks.Single(t => t.Id == first.Id).State);
        Assert.Equal(TaskState.Queued, _stores.Tasks.Single(t => t.Id == second.Id).State);
        Assert.Equal([second.Id], _stores.AdmittedComments.Select(c => c.TaskId));
    }

    [Fact]
    public async Task A_Signal_applied_again_after_a_restart_admits_the_comment_once()
    {
        var (loop, _, _) = await WaitingTaskAsync();
        await TickWithAsync(loop, LoopStores.Comment("comment:500:created"));

        await NewLoop().TickAsync(Token);

        Assert.Single(_stores.AdmittedComments);
        Assert.Equal(TaskState.Queued, Assert.Single(_stores.Tasks).State);
    }
}
