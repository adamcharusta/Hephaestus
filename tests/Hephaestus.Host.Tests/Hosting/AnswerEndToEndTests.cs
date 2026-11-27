using Hephaestus.Core.Escalations;
using Hephaestus.Core.Loop;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Host.Tests.Hosting;

/// <summary>
/// Story 2.10, FR-49: the real host over the real database and a scripted GitHub. A comment of someone who is not a Trusted User leaves a waiting Task
/// where it is, and a Trusted User's reply is recorded as the answer, returns the Task to the Queue, and shows on the issue.
/// </summary>
public class AnswerEndToEndTests
{
    private const long Repository = 101;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<T> EventuallyAsync<T>(Func<Task<T?>> read)
        where T : class
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await read() is { } found)
            {
                return found;
            }

            await Task.Delay(25, Token);
        }

        throw new TimeoutException("The loop did not get there.");
    }

    private static Signal Comment(string id, long commentId, long actor, string login, string body) =>
        new(Signal.GitHubSource, id, SignalKind.CommentCreated, Repository, 7, actor, login, null, commentId, actor, DateTimeOffset.UtcNow.AddMinutes(1), CommentBody: body);

    [Fact]
    public async Task A_stranger_s_comment_leaves_the_Task_waiting_and_a_Trusted_User_s_reply_resumes_it_with_the_answer_attached()
    {
        var github = new FakeCodeHost();
        github.IssueLabels[(Repository, 7)] = ["hephaestus"];
        await using var host = await RunningHost.StartAsync(configure: builder => builder.Services.AddSingleton<ICodeHost>(github));
        var services = host.App.Services;
        var configuration = services.GetRequiredService<IInstallationConfiguration>();
        await configuration.AddTrustedUserAsync(29, "alice", Token);
        await configuration.RegisterProjectAsync(Repository, "acme/shop", "main", Token);
        var reads = services.GetRequiredService<IReadStore>();
        var inbox = services.GetRequiredService<ISignalInbox>();
        await inbox.AppendAsync([new Signal(Signal.GitHubSource, "issue-event:1", SignalKind.Labeled, Repository, 7, 29, "alice", "hephaestus", null, null, DateTimeOffset.UtcNow, "Add a login page", "Use OAuth.")], Token);
        var task = await EventuallyAsync(async () => await reads.FindTaskAsync(Repository, 7, Token));
        Assert.True(await services.GetRequiredService<OrchestratorLoop>().RaiseEscalationAsync(task, EscalationRequest.For("Which database?", ["SQLite", "Postgres"]), "s4", Token));
        await EventuallyAsync(() => Task.FromResult(github.IssueLabels[(Repository, 7)].Contains("hephaestus:waiting-for-owner") ? github.IssueLabels[(Repository, 7)] : null));

        await inbox.AppendAsync([Comment("comment:600:created", 600, 1001, "mallory", "Use MongoDB.")], Token);
        await EventuallyAsync(async () => (await inbox.ReadAsync(0, 100, Token)).FirstOrDefault(s => s.Signal.SourceEventId == "comment:600:created" && s.Status != SignalStatus.Pending) is { } done ? done : null);
        Assert.Equal(TaskState.WaitingForOwner, (await reads.FindTaskAsync(Repository, 7, Token))!.State);
        Assert.Empty(await reads.ListAdmittedCommentsAsync(task.Id, Token));

        await inbox.AppendAsync([Comment("comment:601:created", 601, 29, "alice", "B. Postgres.")], Token);

        await EventuallyAsync(async () => (await reads.FindTaskAsync(Repository, 7, Token)) is { State: TaskState.Queued } resumed ? resumed : null);
        var answer = Assert.Single(await reads.ListAdmittedCommentsAsync(task.Id, Token));
        var escalation = Assert.Single(await reads.ListEscalationsAsync(task.Id, Token));
        Assert.Equal((601L, 29L, "B. Postgres.", escalation.Id), (answer.CommentId, answer.AuthorId, answer.Body, answer.AnswersEscalationId));
        Assert.Equal(EscalationOutcome.Answered, escalation.Outcome);
        await EventuallyAsync(() => Task.FromResult(github.IssueLabels[(Repository, 7)].Contains("hephaestus:queued") ? github.IssueLabels[(Repository, 7)] : null));
        Assert.DoesNotContain("hephaestus:waiting-for-owner", github.IssueLabels[(Repository, 7)]);
        Assert.Single(github.Comments);
    }
}
