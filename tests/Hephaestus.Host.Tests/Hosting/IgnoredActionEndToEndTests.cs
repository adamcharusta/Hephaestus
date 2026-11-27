using Hephaestus.Core.Effects;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Host.Tests.Hosting;

/// <summary>
/// Story 2.5, AD-5, AD-15, NFR-10: the real host over the real database and a scripted GitHub. A label from someone who is not a Trusted User is
/// said on the issue to have been ignored, once; their comment is recorded and gets no reply; and what is replayed from untrusted actors changes
/// no Task, while a Trusted User whose login was renamed is still recognized by the numeric id.
/// </summary>
public class IgnoredActionEndToEndTests
{
    private const long Repository = 101;
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Signal Labeled(string id, long actor, string login, int issue = 7) =>
        new(Signal.GitHubSource, id, SignalKind.Labeled, Repository, issue, actor, login, "hephaestus", null, null, Noon, "Add a login page", "Use OAuth.");

    private static Signal Comment(string id, long actor, string login) =>
        new(Signal.GitHubSource, id, SignalKind.CommentCreated, Repository, 7, actor, login, null, 5, actor, Noon);

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

    private static Task<RunningHost> StartAsync(FakeCodeHost github, Language language = Language.English) =>
        RunningHost.StartAsync(language, configure: builder => builder.Services.AddSingleton<ICodeHost>(github));

    [Fact]
    public async Task A_label_from_someone_who_is_not_a_Trusted_User_gets_one_comment_that_says_why_and_the_journal_holds_its_intent_and_result()
    {
        var github = new FakeCodeHost();
        await using var host = await StartAsync(github);
        var services = host.App.Services;
        await SetUpAsync(services);
        var inbox = services.GetRequiredService<ISignalInbox>();

        await inbox.AppendAsync([Labeled("issue-event:1", 1001, "bob")], Token);
        await EventuallyAsync(() => Task.FromResult(github.Comments.Count == 1 ? github.Comments : null));
        await inbox.AppendAsync([Labeled("issue-event:2", 1001, "bob"), Labeled("issue-event:3", 1002, "carol")], Token);
        await EventuallyAsync(() => Task.FromResult(github.Comments.Count == 2 ? github.Comments : null));
        await EventuallyAsync(async () => (await services.GetRequiredService<ISignalInbox>().ReadPendingAsync(10, Token)) is { Count: 0 } none ? none : null);

        var reads = services.GetRequiredService<IReadStore>();
        Assert.Null(await reads.FindTaskAsync(Repository, 7, Token));
        Assert.Equal(
            ["**Ignored.** `bob` is not a Trusted User, so the `hephaestus` label has no effect.", "**Ignored.** `carol` is not a Trusted User, so the `hephaestus` label has no effect."],
            github.Comments.Select(c => c.Text));
        var journal = await EventuallyAsync<IReadOnlyList<JournalEntry>>(async () => await reads.ListJournalAsync(null, Token) is { Count: 4 } all ? all : null);
        Assert.Equal([JournalEntryKind.Intent, JournalEntryKind.Result, JournalEntryKind.Intent, JournalEntryKind.Result], journal.Select(e => e.Entry));
        Assert.Equal(["project:101:g1:comment:0", "project:101:g1:comment:0", "project:101:g3:comment:0", "project:101:g3:comment:0"], journal.Select(e => e.Effect.Key));
        Assert.All(journal.Where(e => e.Entry == JournalEntryKind.Result), e => Assert.Equal(EffectResult.Applied, e.Result));
        Assert.Equal(github.Comments.Select(c => c.Key), [journal[0].Effect.Key, journal[2].Effect.Key]);
    }

    [Fact]
    public async Task The_text_is_in_the_language_the_environment_names_when_the_Owner_has_stored_none()
    {
        var github = new FakeCodeHost();
        await using var host = await StartAsync(github, Language.Polish);
        await SetUpAsync(host.App.Services);

        await host.App.Services.GetRequiredService<ISignalInbox>().AppendAsync([Labeled("issue-event:1", 1001, "bob")], Token);

        var comments = await EventuallyAsync(() => Task.FromResult(github.Comments.Count == 1 ? github.Comments : null));
        Assert.Equal("**Zignorowano.** `bob` nie jest zaufanym użytkownikiem (Trusted User), więc etykieta `hephaestus` nie ma żadnego skutku.", comments[0].Text);
    }

    [Fact]
    public async Task Recorded_events_of_untrusted_actors_change_no_Task_and_get_no_reply_but_the_label_and_a_renamed_Trusted_User_is_still_recognized_by_id()
    {
        var github = new FakeCodeHost();
        github.IssueLabels[(Repository, 8)] = ["hephaestus"];
        await using var host = await StartAsync(github);
        var services = host.App.Services;
        await SetUpAsync(services);
        var inbox = services.GetRequiredService<ISignalInbox>();

        await inbox.AppendAsync(
            [
                Comment("comment:5:created", 1001, "bob"),
                Comment("comment:6:created", 1001, "bob"),
                Labeled("issue-event:1", 1003, "mallory", issue: 8),
                Labeled("issue-event:2", 29, "alice-renamed", issue: 8),
            ],
            Token);

        var reads = services.GetRequiredService<IReadStore>();
        var task = await EventuallyAsync(async () => await reads.FindTaskAsync(Repository, 8, Token));
        await EventuallyAsync(async () => (await inbox.ReadPendingAsync(10, Token)) is { Count: 0 } none ? none : null);
        Assert.Equal(TaskState.Queued, task.State);
        Assert.Null(await reads.FindTaskAsync(Repository, 7, Token));
        var stored = await inbox.ReadAsync(0, 10, Token);
        Assert.Equal(
            [(SignalStatus.Rejected, "actor-not-trusted", 1001L), (SignalStatus.Rejected, "actor-not-trusted", 1001L), (SignalStatus.Rejected, "actor-not-trusted", 1003L), (SignalStatus.Applied, null, 29L)],
            stored.Select(s => (s.Status, s.Reason, s.Signal.ActorId!.Value)));
        var said = await EventuallyAsync(() => Task.FromResult(github.Comments.Count == 1 ? github.Comments : null));
        Assert.Equal((8, true), (said[0].IssueNumber, said[0].Text.Contains("`mallory`", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_GitHub_that_refuses_the_comment_leaves_the_refusal_in_the_journal_and_the_loop_goes_on()
    {
        var github = new FakeCodeHost { CommentFails = Hephaestus.Core.GitHub.GitHubIssueFailure.IssueNotFound };
        await using var host = await StartAsync(github);
        var services = host.App.Services;
        await SetUpAsync(services);
        var reads = services.GetRequiredService<IReadStore>();

        await services.GetRequiredService<ISignalInbox>().AppendAsync([Labeled("issue-event:1", 1001, "bob")], Token);

        var journal = await EventuallyAsync<IReadOnlyList<JournalEntry>>(async () => await reads.ListJournalAsync(null, Token) is { Count: 2 } all ? all : null);
        Assert.Equal((EffectResult.Refused, "issue-not-found"), (journal[1].Result, journal[1].Reason));
        Assert.Empty(github.Comments);
        Assert.Empty(await reads.ListUnfinishedEffectsAsync(Token));
    }
}
