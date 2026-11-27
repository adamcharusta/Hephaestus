using System.Net;
using System.Security.Cryptography;
using System.Text;
using Hephaestus.Core.GitHub;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.GitHub;
using Hephaestus.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Host.Tests.Hosting;

/// <summary>
/// Story 2.1, AD-1: the poller over the real database. A Project that is registered, labelled on GitHub, and polled leaves its
/// Signal in the inbox; a host that restarts, or polls the same events again, neither loses nor doubles it.
/// </summary>
public sealed class PollingEndToEndTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("hxpoll").FullName;
    private readonly List<ServiceProvider> _providers = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>GitHub with one installation holding repository 101, and the events and comments a test puts there.</summary>
    private sealed class Github : HttpMessageHandler
    {
        public string Events { get; set; } = "[]";

        public string Comments { get; set; } = "[]";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var (status, body) = path switch
            {
                "/app/installations" => (HttpStatusCode.OK, """[{"id":10,"account":{"login":"acme","id":2,"type":"Organization"},"target_type":"Organization","repository_selection":"all"}]"""),
                "/app/installations/10/access_tokens" => (HttpStatusCode.Created, """{"token":"ghs_installation-token-0123456789","expires_at":"2099-01-01T00:00:00Z"}"""),
                "/installation/repositories" => (HttpStatusCode.OK, """{"total_count":1,"repositories":[{"id":101,"full_name":"acme/shop","name":"shop"}]}"""),
                "/repositories/101/issues/events" => (HttpStatusCode.OK, Events),
                "/repositories/101/issues/comments" => (HttpStatusCode.OK, Comments),
                _ => (HttpStatusCode.NotFound, """{"message":"Not Found"}"""),
            };
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class MovingTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly MovingTime _time = new(Noon);

    private ServiceProvider StartHost()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        var provider = services.AddHephaestusPersistence(new PersistenceOptions(Path.Combine(_directory, "data"), Path.Combine(_directory, "secrets"))).BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    private sealed class CrashingCursors(IPollCursorStore inner) : IPollCursorStore
    {
        public Task<PollCursor?> GetAsync(long repositoryId, string stream, CancellationToken cancellationToken) => inner.GetAsync(repositoryId, stream, cancellationToken);

        public Task SetAsync(long repositoryId, string stream, PollCursor cursor, CancellationToken cancellationToken) => throw new InvalidOperationException("the process ended here");
    }

    private static GitHubPoller PollerOver(ServiceProvider host, Github github, IPollCursorStore? cursors = null) =>
        new(
            host.GetRequiredService<IReadStore>(),
            host.GetRequiredService<ISecretStore>(),
            host.GetRequiredService<ISignalInbox>(),
            cursors ?? host.GetRequiredService<IPollCursorStore>(),
            host.GetRequiredService<TimeProvider>(),
            new GitHubRateLimit(host.GetRequiredService<TimeProvider>()),
            () => github);

    private async Task<ServiceProvider> InstallAsync()
    {
        var host = StartHost();
        await host.GetRequiredService<DatabaseMigrator>().MigrateAsync(Token);
        await host.GetRequiredService<ISecretStore>().SetAsync(GitHubAppCredential.PrivateKeySecretName, RSA.Create(2048).ExportRSAPrivateKeyPem(), Token);
        await host.GetRequiredService<IInstallationConfiguration>().SetGitHubAppIdAsync(123456, "hephaestus-test", Token);
        await host.GetRequiredService<IInstallationConfiguration>().RegisterProjectAsync(101, "acme/shop", "main", Token);
        return host;
    }

    [Fact]
    public async Task An_event_on_a_registered_Project_becomes_one_Signal_in_the_database_with_the_actors_numeric_id()
    {
        var github = new Github { Events = """[{"id":501,"actor":{"login":"alice","id":583231},"event":"labeled","created_at":"2026-11-27T12:05:00Z","label":{"name":"hephaestus"},"issue":{"number":7}}]""" };
        var host = await InstallAsync();
        using var poller = PollerOver(host, github);

        var report = await poller.PollOnceAsync(Token);

        Assert.Equal(1, report.SignalsAppended);
        var stored = Assert.Single(await host.GetRequiredService<ISignalInbox>().ReadAsync(0, 10, Token));
        Assert.Equal(new Signal("github", "issue-event:501", SignalKind.Labeled, 101, 7, 583231, "alice", "hephaestus", null, null, new DateTimeOffset(2026, 11, 27, 12, 5, 0, TimeSpan.Zero)), stored.Signal);
        Assert.Equal("501", (await host.GetRequiredService<IPollCursorStore>().GetAsync(101, PollStreams.IssueEvents, Token))!.Position);
    }

    [Fact]
    public async Task A_host_that_restarts_and_polls_the_same_events_again_appends_nothing_twice_and_loses_nothing()
    {
        var github = new Github
        {
            Events = """[{"id":501,"actor":{"login":"alice","id":583231},"event":"closed","created_at":"2026-11-27T12:05:00Z","issue":{"number":7}}]""",
            Comments = """[{"id":9,"user":{"login":"alice","id":583231},"created_at":"2026-11-27T12:06:00Z","updated_at":"2026-11-27T12:06:00Z","issue_url":"https://api.github.com/repos/acme/shop/issues/7"}]""",
        };
        var first = await InstallAsync();

        // The process ends after the append and before the cursor is written.
        using (var poller = PollerOver(first, github, cursors: new CrashingCursors(first.GetRequiredService<IPollCursorStore>())))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => poller.PollOnceAsync(Token));
        }

        Assert.Single(await first.GetRequiredService<ISignalInbox>().ReadAsync(0, 10, Token));
        Assert.Null(await first.GetRequiredService<IPollCursorStore>().GetAsync(101, PollStreams.IssueEvents, Token));

        var restarted = StartHost();
        using var again = PollerOver(restarted, github);
        var report = await again.PollOnceAsync(Token);

        Assert.Equal(1, report.SignalsAppended);
        var signals = await restarted.GetRequiredService<ISignalInbox>().ReadAsync(0, 10, Token);
        Assert.Equal(["issue-event:501", "comment:9:created"], signals.Select(s => s.Signal.SourceEventId));
        Assert.Equal("501", (await restarted.GetRequiredService<IPollCursorStore>().GetAsync(101, PollStreams.IssueEvents, Token))!.Position);
    }

    [Fact]
    public async Task A_Project_that_was_removed_is_not_polled_and_one_registered_again_reads_only_what_happens_after_that()
    {
        var github = new Github { Events = """[{"id":501,"actor":{"login":"alice","id":583231},"event":"closed","created_at":"2026-11-27T12:05:00Z","issue":{"number":7}}]""" };
        var host = await InstallAsync();
        await host.GetRequiredService<IInstallationConfiguration>().RemoveProjectAsync(101, Token);
        _time.Now = Noon.AddMinutes(10);
        using var poller = PollerOver(host, github);

        var removed = await poller.PollOnceAsync(Token);

        Assert.Equal(0, removed.ProjectsRead);
        Assert.Empty(await host.GetRequiredService<ISignalInbox>().ReadAsync(0, 10, Token));

        await host.GetRequiredService<IInstallationConfiguration>().RegisterProjectAsync(101, "acme/shop", "main", Token);
        var registered = await poller.PollOnceAsync(Token);

        // The event is from 12:05, while the Project was not registered; Hephaestus has been watching since 12:10, so it is not news.
        Assert.Equal((1, 0), (registered.ProjectsRead, registered.SignalsAppended));
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        Directory.Delete(_directory, recursive: true);
    }
}
