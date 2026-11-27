using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.Loop;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Hephaestus.Host.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Host.Tests.Hosting;

/// <summary>
/// Story 2.9, FR-48, AR-63: the real host over the real database and a scripted GitHub. An Escalation raised for a Task is recorded, said on the
/// issue in its fixed shape, and shown by the Task's label; the notifier of v0.1 does nothing and the outcome is the same; another Task goes on;
/// and a Trusted User's removal of the Marker Label ends the wait.
/// </summary>
public class EscalationEndToEndTests
{
    private const long Repository = 101;
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Signal Labeled(string id, int issue) =>
        new(Signal.GitHubSource, id, SignalKind.Labeled, Repository, issue, 29, "alice", "hephaestus", null, null, Noon, "Add a login page", "Use OAuth.");

    private static Signal Unlabeled(string id, int issue, long actor, string login) =>
        new(Signal.GitHubSource, id, SignalKind.Unlabeled, Repository, issue, actor, login, "hephaestus", null, null, Noon);

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

    private static async Task SetUpAsync(IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IInstallationConfiguration>();
        await configuration.AddTrustedUserAsync(29, "alice", Token);
        await configuration.RegisterProjectAsync(Repository, "acme/shop", "main", Token);
    }

    private static Task<RunningHost> StartAsync(FakeCodeHost github)
    {
        // The issues that are opted in carry the Marker Label, as they do on GitHub.
        github.IssueLabels[(Repository, 7)] = ["hephaestus"];
        github.IssueLabels[(Repository, 8)] = ["hephaestus"];
        return RunningHost.StartAsync(configure: builder => builder.Services.AddSingleton<ICodeHost>(github));
    }

    [Fact]
    public async Task The_host_has_a_notifier_that_does_nothing_and_the_Escalation_is_made_all_the_same()
    {
        var github = new FakeCodeHost();
        await using var host = await StartAsync(github);
        var services = host.App.Services;
        await SetUpAsync(services);
        Assert.IsType<NoOpNotifier>(services.GetRequiredService<INotifier>());
        var reads = services.GetRequiredService<IReadStore>();
        await services.GetRequiredService<ISignalInbox>().AppendAsync([Labeled("issue-event:1", 7)], Token);
        var task = await EventuallyAsync(async () => await reads.FindTaskAsync(Repository, 7, Token));

        Assert.True(await services.GetRequiredService<OrchestratorLoop>().RaiseEscalationAsync(task, EscalationRequest.For("Which database?", ["SQLite", "Postgres"]), "s4", Token));

        var journal = await EventuallyAsync<IReadOnlyList<JournalEntry>>(async () =>
            await reads.ListJournalAsync(task.Id, Token) is var all && all.Count(e => e.Entry == JournalEntryKind.Result) == 4 ? all : null);
        Assert.Equal(
            "**Hephaestus needs a decision.**\n\nWhich database?\nA. SQLite\nB. Postgres\n\nReply on this issue. Any reply from a Trusted User returns the Task to the Queue with the answer.",
            github.Comments[0].Text);
        Assert.Equal($"{task.Id}:s4:comment:0", github.Comments[0].Key);
        Assert.Equal(
            [EffectResult.Applied, EffectResult.Applied, EffectResult.Applied, EffectResult.Applied],
            journal.Where(e => e.Entry == JournalEntryKind.Result).Select(e => e.Result!.Value));
        Assert.Equal([EffectKind.Label, EffectKind.Label, EffectKind.Comment, EffectKind.Notification], journal.Where(e => e.Entry == JournalEntryKind.Intent).Select(e => e.Effect.Kind));
        Assert.Equal(TaskState.WaitingForOwner, (await reads.FindTaskAsync(Repository, 7, Token))!.State);
        Assert.Contains("hephaestus:waiting-for-owner", github.IssueLabels[(Repository, 7)]);
        var escalation = Assert.Single(await reads.ListEscalationsAsync(task.Id, Token));
        Assert.Equal((EscalationKind.Decision, ResumeConditions.TrustedReply), (escalation.Kind, escalation.ResumeCondition));
    }

    [Fact]
    public async Task Another_Task_goes_on_while_one_waits_and_a_Trusted_User_who_removes_the_label_ends_the_wait()
    {
        var github = new FakeCodeHost();
        await using var host = await StartAsync(github);
        var services = host.App.Services;
        await SetUpAsync(services);
        var reads = services.GetRequiredService<IReadStore>();
        var inbox = services.GetRequiredService<ISignalInbox>();
        await inbox.AppendAsync([Labeled("issue-event:1", 7)], Token);
        var waiting = await EventuallyAsync(async () => await reads.FindTaskAsync(Repository, 7, Token));
        await services.GetRequiredService<OrchestratorLoop>().RaiseEscalationAsync(waiting, EscalationRequest.For("Which database?", []), "s4", Token);

        await inbox.AppendAsync([Labeled("issue-event:2", 8)], Token);
        var other = await EventuallyAsync(async () => await reads.FindTaskAsync(Repository, 8, Token));
        await inbox.AppendAsync([Unlabeled("issue-event:3", 7, 1001, "bob"), Unlabeled("issue-event:4", 7, 29, "alice")], Token);

        await EventuallyAsync(async () => (await reads.FindTaskAsync(Repository, 7, Token)) is { State: TaskState.Cancelled } done ? done : null);
        Assert.Equal(TaskState.Queued, (await reads.FindTaskAsync(Repository, 8, Token))!.State);
        Assert.Equal([other.Id], DispatchPolicy.Eligible([(await reads.FindTaskAsync(Repository, 7, Token))!, (await reads.FindTaskAsync(Repository, 8, Token))!]).Select(t => t.Id));
        var closed = Assert.Single(await reads.ListEscalationsAsync(waiting.Id, Token));
        Assert.Equal(EscalationOutcome.Cancelled, closed.Outcome);
        await EventuallyAsync(() => Task.FromResult(github.IssueLabels[(Repository, 7)].Contains("hephaestus:cancelled") ? github.IssueLabels[(Repository, 7)] : null));
    }
}
