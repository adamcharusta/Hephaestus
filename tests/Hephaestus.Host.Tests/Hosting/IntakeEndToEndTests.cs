using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Hephaestus.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Host.Tests.Hosting;

/// <summary>
/// Story 2.2, AD-1, AD-2, AD-15: the real host over the real database. A Marker Label event of a Trusted User becomes a Queued Task
/// with its snapshot and branch; anyone else's is recorded as rejected; a Signal appended before a restart is applied by the first tick.
/// </summary>
public class IntakeEndToEndTests
{
    private const long Repository = 101;
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Signal Labeled(string id, long actor, string login, int issue = 7, string title = "Add a login page") =>
        new(Signal.GitHubSource, id, SignalKind.Labeled, Repository, issue, actor, login, "hephaestus", null, null, Noon, title, "Use OAuth.");

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

    [Fact]
    public async Task A_Trusted_Users_Marker_Label_becomes_a_Queued_Task_with_its_snapshot_and_branch_and_anyone_elses_is_only_recorded()
    {
        await using var host = await RunningHost.StartAsync();
        var services = host.App.Services;
        await SetUpAsync(services);
        var inbox = services.GetRequiredService<ISignalInbox>();
        var reads = services.GetRequiredService<IReadStore>();

        await inbox.AppendAsync([Labeled("issue-event:1", 500, "mallory", issue: 6), Labeled("issue-event:2", 29, "alice")], Token);

        var task = await EventuallyAsync(() => reads.FindTaskAsync(Repository, 7, Token));
        Assert.Equal((TaskState.Queued, "hephaestus/7-add-a-login-page"), (task.State, task.Branch));
        var snapshot = await reads.FindIssueSnapshotAsync(task.Id, Token);
        Assert.Equal(("Add a login page", "Use OAuth.", IssueContent.Hash("Add a login page", "Use OAuth.")), (snapshot?.Title, snapshot?.Body, snapshot?.Hash));
        Assert.Null(await reads.FindTaskAsync(Repository, 6, Token));
        var stored = await EventuallyAsync<IReadOnlyList<StoredSignal>>(async () => (await inbox.ReadAsync(0, 10, Token)) is { Count: 2 } all && all.All(s => s.Status != SignalStatus.Pending) ? all : null);
        Assert.Equal((SignalStatus.Rejected, SignalRejections.ActorNotTrusted, 500L), (stored[0].Status, stored[0].Reason, stored[0].Signal.ActorId));
        Assert.Equal((SignalStatus.Applied, task.Id), (stored[1].Status, stored[1].TaskId));
    }

    [Fact]
    public async Task A_Signal_appended_before_a_restart_is_applied_by_the_first_tick_of_the_next_host()
    {
        using var installation = new TempSocket();
        await using (var first = await RunningHost.StartAsync(installation: installation))
        {
            await SetUpAsync(first.App.Services);
        }

        using (var offline = new ServiceCollection().AddHephaestusPersistence(new PersistenceOptions(installation.DataDirectory, installation.SecretsDirectory)).BuildServiceProvider())
        {
            await offline.GetRequiredService<ISignalInbox>().AppendAsync([Labeled("issue-event:1", 29, "alice")], Token);
            Assert.Single(await offline.GetRequiredService<ISignalInbox>().ReadPendingAsync(10, Token));
        }

        await using var second = await RunningHost.StartAsync(installation: installation);
        var task = await EventuallyAsync(() => second.App.Services.GetRequiredService<IReadStore>().FindTaskAsync(Repository, 7, Token));

        Assert.Equal(TaskState.Queued, task.State);
        Assert.Empty(await second.App.Services.GetRequiredService<ISignalInbox>().ReadPendingAsync(10, Token));
    }

    [Fact]
    public async Task The_same_event_appended_again_after_it_was_applied_makes_no_second_Task()
    {
        await using var host = await RunningHost.StartAsync();
        var services = host.App.Services;
        await SetUpAsync(services);
        var inbox = services.GetRequiredService<ISignalInbox>();
        var reads = services.GetRequiredService<IReadStore>();
        await inbox.AppendAsync([Labeled("issue-event:1", 29, "alice")], Token);
        var task = await EventuallyAsync(() => reads.FindTaskAsync(Repository, 7, Token));

        Assert.Equal(0, await inbox.AppendAsync([Labeled("issue-event:1", 29, "alice")], Token));
        await inbox.AppendAsync([Labeled("issue-event:2", 29, "alice", title: "Another title")], Token);
        await EventuallyAsync<IReadOnlyList<StoredSignal>>(async () => await inbox.ReadAsync(0, 10, Token) is { Count: 2 } all && all.All(s => s.Status != SignalStatus.Pending) ? all : null);

        Assert.Equal(task, await reads.FindTaskAsync(Repository, 7, Token));
        Assert.Equal("Add a login page", (await reads.FindIssueSnapshotAsync(task.Id, Token))?.Title);
    }
}
