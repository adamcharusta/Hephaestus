using Hephaestus.Core.Effects;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Host.Tests.Hosting;

/// <summary>
/// Story 2.4, FR-11, AD-2, AD-5: the real host over the real database and a scripted GitHub. A Task's state is shown on its issue as one
/// label, the label is put back when it is changed by hand, and the journal holds the intent and the result of each change.
/// </summary>
public class StateLabelEndToEndTests
{
    private const long Repository = 101;
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Signal Labeled(string id, string label = "hephaestus", long actor = 29, string login = "alice", int issue = 7) =>
        new(Signal.GitHubSource, id, SignalKind.Labeled, Repository, issue, actor, login, label, null, null, Noon, "Add a login page", "Use OAuth.");

    private static Signal Unlabeled(string id, string label, int issue = 7) =>
        new(Signal.GitHubSource, id, SignalKind.Unlabeled, Repository, issue, 1001, "bob", label, null, null, Noon);

    private static async Task SetUpAsync(IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IInstallationConfiguration>();
        await configuration.AddTrustedUserAsync(29, "alice", Token);
        await configuration.RegisterProjectAsync(Repository, "acme/shop", "main", Token);
    }

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

    private static Task<RunningHost> StartAsync(FakeCodeHost github, TempSocket? installation = null) =>
        RunningHost.StartAsync(configure: builder => builder.Services.AddSingleton<ICodeHost>(github), installation: installation);

    private static Task<List<string>> LabelsAsync(FakeCodeHost github, Func<List<string>, bool> done) =>
        EventuallyAsync(() => Task.FromResult(github.IssueLabels.TryGetValue((Repository, 7), out var labels) && done(labels) ? labels : null));

    [Fact]
    public async Task An_opted_in_issue_shows_its_state_and_the_journal_holds_the_intent_and_the_result_of_the_change()
    {
        var github = new FakeCodeHost();
        github.IssueLabels[(Repository, 7)] = ["hephaestus"];
        await using var host = await StartAsync(github);
        var services = host.App.Services;
        await SetUpAsync(services);

        await services.GetRequiredService<ISignalInbox>().AppendAsync([Labeled("issue-event:1")], Token);

        await LabelsAsync(github, labels => labels.Contains("hephaestus:queued"));
        var reads = services.GetRequiredService<IReadStore>();
        var task = (await reads.FindTaskAsync(Repository, 7, Token))!;
        var journal = await EventuallyAsync<IReadOnlyList<JournalEntry>>(async () => await reads.ListJournalAsync(task.Id, Token) is { Count: 2 } all ? all : null);
        Assert.Equal([JournalEntryKind.Intent, JournalEntryKind.Result], journal.Select(e => e.Entry));
        Assert.Equal($"{task.Id}:g1:label:0", journal[0].Effect.Key);
        Assert.Equal(EffectResult.Applied, journal[1].Result);
        Assert.Equal(journal[0].Effect.Key, Assert.Single(github.LabelChanges).Key);
        Assert.Equal(TaskState.Queued, task.State);
    }

    [Fact]
    public async Task A_state_label_taken_off_by_hand_is_written_back()
    {
        var github = new FakeCodeHost();
        github.IssueLabels[(Repository, 7)] = ["hephaestus"];
        await using var host = await StartAsync(github);
        var services = host.App.Services;
        await SetUpAsync(services);
        var inbox = services.GetRequiredService<ISignalInbox>();
        await inbox.AppendAsync([Labeled("issue-event:1")], Token);
        await LabelsAsync(github, labels => labels.Contains("hephaestus:queued"));

        github.IssueLabels[(Repository, 7)].Remove("hephaestus:queued");
        await inbox.AppendAsync([Unlabeled("issue-event:2", "hephaestus:queued")], Token);

        var labels = await LabelsAsync(github, l => l.Contains("hephaestus:queued"));
        Assert.Equal(["hephaestus", "hephaestus:queued"], labels.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_issue_that_was_deleted_leaves_the_Task_waiting_for_the_Owner_and_the_refusal_in_the_journal()
    {
        var github = new FakeCodeHost();
        await using var host = await StartAsync(github);
        var services = host.App.Services;
        await SetUpAsync(services);
        var reads = services.GetRequiredService<IReadStore>();

        await services.GetRequiredService<ISignalInbox>().AppendAsync([Labeled("issue-event:1")], Token);

        var task = await EventuallyAsync(async () => await reads.FindTaskAsync(Repository, 7, Token) is { State: TaskState.WaitingForOwner } waiting ? waiting : null);
        var journal = await EventuallyAsync<IReadOnlyList<JournalEntry>>(async () => await reads.ListJournalAsync(task.Id, Token) is { Count: >= 4 } all ? all : null);
        var refusal = journal.First(e => e.Entry == JournalEntryKind.Result);
        Assert.Equal((EffectResult.Refused, "issue-not-found"), (refusal.Result, refusal.Reason));
    }

    [Fact]
    public async Task An_effect_waiting_for_its_next_attempt_when_the_host_stopped_is_still_there_with_its_recorded_time_when_the_next_one_starts()
    {
        using var installation = new TempSocket();
        var unreachable = new FakeCodeHost { IssueFails = Hephaestus.Core.GitHub.GitHubIssueFailure.Unreachable };
        PendingEffect before;
        await using (var first = await StartAsync(unreachable, installation))
        {
            await SetUpAsync(first.App.Services);
            await first.App.Services.GetRequiredService<ISignalInbox>().AppendAsync([Labeled("issue-event:1")], Token);
            var reads = first.App.Services.GetRequiredService<IReadStore>();
            before = await EventuallyAsync(async () => (await reads.ListUnfinishedEffectsAsync(Token)).FirstOrDefault(p => p.Failures == 1));
        }

        var github = new FakeCodeHost();
        github.IssueLabels[(Repository, 7)] = ["hephaestus", "hephaestus:queued"];
        await using var second = await StartAsync(github, installation);

        var after = Assert.Single(await second.App.Services.GetRequiredService<IReadStore>().ListUnfinishedEffectsAsync(Token));
        Assert.Equal((before.Effect.Key, 1, before.NextAttemptAt), (after.Effect.Key, after.Failures, after.NextAttemptAt));
        Assert.NotNull(after.NextAttemptAt);
        Assert.Equal(TaskState.Queued, after.Task!.State);
        Assert.Empty(github.LabelChanges);
    }
}
