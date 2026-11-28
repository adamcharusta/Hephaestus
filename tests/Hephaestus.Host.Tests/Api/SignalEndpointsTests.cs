using System.Net;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Signals;
using Hephaestus.Host.Cli;

namespace Hephaestus.Host.Tests.Api;

/// <summary>
/// Story 3.14, AD-17, FR-16: starting and stopping work on a Project are Signals, answered with 202 and the Signal's id; the status of a Signal is read as pending, applied, or rejected with its reason; and a window is what the loop's application of the
/// Signal leaves.
/// </summary>
public class SignalEndpointsTests
{
    private const long Repository = 101;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task<RunningHost> StartAsync(FakeCodeHost? gateway = null, Language language = Language.English) =>
        RunningHost.StartAsync(language, configure: builder => builder.Services.AddSingleton<ICodeHost>(gateway ?? new FakeCodeHost()));

    private static ControlApiClient ClientFor(RunningHost host, Language language = Language.English) => new(host.Settings.ControlSocketPath, language);

    private static async Task RegisterAsync(RunningHost host, string name = "acme/shop")
    {
        using var client = ClientFor(host);
        var answer = await client.SendAsync(HttpMethod.Put, $"/api/projects?name={name}", """{"baseBranch":"main"}""", Token);
        Assert.Equal(HttpStatusCode.Created, answer.Status);
    }

    private static async Task<string> SettledAsync(ControlApiClient client, string id)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var answer = await client.SendAsync(HttpMethod.Get, $"/api/signals/{id}", null, Token);
            if (!answer.Body.Contains("\"status\":\"pending\"", StringComparison.Ordinal))
            {
                return answer.Body;
            }

            await Task.Delay(50, Token);
        }

        throw new TimeoutException("The loop did not apply the Signal.");
    }

    [Fact]
    public async Task A_start_is_answered_with_202_and_the_id_of_a_pending_Signal_that_the_inbox_holds()
    {
        await using var host = await StartAsync();
        await RegisterAsync(host);
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Post, "/api/projects/start?name=acme/shop", null, Token);

        Assert.Equal(HttpStatusCode.Accepted, answer.Status);
        Assert.Matches("""^\{"id":\d+,"kind":"start-requested","status":"pending","reason":null\}$""", answer.Body);
        var stored = (await host.App.Services.GetRequiredService<ISignalInbox>().ReadAsync(0, 10, Token)).Single(s => s.Signal.Kind == SignalKind.StartRequested);
        Assert.Equal((Signal.ControlSource, Repository, 0, null), (stored.Signal.Source, stored.Signal.RepositoryId, stored.Signal.IssueNumber, stored.Signal.ActorId));
        Assert.Contains($"\"id\":{stored.Sequence},", answer.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stop_is_a_Signal_of_its_own_kind_and_each_request_is_one_Signal_even_when_the_same_is_asked_twice()
    {
        await using var host = await StartAsync();
        await RegisterAsync(host);
        using var client = ClientFor(host);

        var first = await client.SendAsync(HttpMethod.Post, "/api/projects/stop?name=acme/shop", null, Token);
        var second = await client.SendAsync(HttpMethod.Post, "/api/projects/stop?name=acme/shop", null, Token);

        Assert.All(new[] { first, second }, answer => Assert.Equal(HttpStatusCode.Accepted, answer.Status));
        Assert.Contains("\"kind\":\"stop-requested\"", first.Body, StringComparison.Ordinal);
        Assert.NotEqual(first.Body, second.Body);
    }

    [Fact]
    public async Task The_loop_applies_the_Signal_and_its_status_is_read_as_applied_with_the_window_it_opened()
    {
        var gateway = new FakeCodeHost();
        gateway.IssueLabels[(Repository, 7)] = ["hephaestus"];
        await using var host = await StartAsync(gateway);
        await RegisterAsync(host);
        var services = host.App.Services;
        await services.GetRequiredService<IInstallationConfiguration>().AddTrustedUserAsync(29, "alice", Token);
        // A Task waits in the Queue and cannot start (the fake GitHub has no file for it), so that the window is not drained at once.
        await services.GetRequiredService<ISignalInbox>().AppendAsync([new Signal(Signal.GitHubSource, "issue-event:1", SignalKind.Labeled, Repository, 7, 29, "alice", "hephaestus", null, null, DateTimeOffset.UtcNow, "Add a login page", "Use OAuth.")], Token);
        using var client = ClientFor(host);
        var accepted = await client.SendAsync(HttpMethod.Post, "/api/projects/start?name=acme/shop", null, Token);
        var id = System.Text.RegularExpressions.Regex.Match(accepted.Body, @"\d+").Value;

        var settled = await SettledAsync(client, id);

        Assert.Equal($$"""{"id":{{id}},"kind":"start-requested","status":"applied","reason":null}""", settled);
        var window = Assert.Single(await services.GetRequiredService<IReadStore>().ListWindowsAsync(openOnly: true, Token));
        Assert.Equal(Repository, window.RepositoryId);
    }

    [Fact]
    public async Task A_second_start_is_applied_with_the_note_that_a_window_is_open_already()
    {
        var gateway = new FakeCodeHost();
        gateway.IssueLabels[(Repository, 7)] = ["hephaestus"];
        await using var host = await StartAsync(gateway);
        await RegisterAsync(host);
        var services = host.App.Services;
        await services.GetRequiredService<IInstallationConfiguration>().AddTrustedUserAsync(29, "alice", Token);
        await services.GetRequiredService<ISignalInbox>().AppendAsync([new Signal(Signal.GitHubSource, "issue-event:1", SignalKind.Labeled, Repository, 7, 29, "alice", "hephaestus", null, null, DateTimeOffset.UtcNow, "Add a login page", "Use OAuth.")], Token);
        using var client = ClientFor(host);
        await SettledAsync(client, System.Text.RegularExpressions.Regex.Match((await client.SendAsync(HttpMethod.Post, "/api/projects/start?name=acme/shop", null, Token)).Body, @"\d+").Value);

        var second = await client.SendAsync(HttpMethod.Post, "/api/projects/start?name=acme/shop", null, Token);
        var settled = await SettledAsync(client, System.Text.RegularExpressions.Regex.Match(second.Body, @"\d+").Value);

        Assert.Contains("\"status\":\"applied\",\"reason\":\"window-already-open\"", settled, StringComparison.Ordinal);
        Assert.Single(await services.GetRequiredService<IReadStore>().ListWindowsAsync(openOnly: false, Token));
    }

    [Fact]
    public async Task A_Signal_the_loop_rejected_is_read_as_rejected_with_the_reason()
    {
        await using var host = await StartAsync();
        using var client = ClientFor(host);
        var inbox = host.App.Services.GetRequiredService<ISignalInbox>();
        var id = await inbox.AppendOneAsync(Signal.Start(999, Guid.NewGuid(), DateTimeOffset.UtcNow), Token);

        var settled = await SettledAsync(client, id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal($$"""{"id":{{id}},"kind":"start-requested","status":"rejected","reason":"project-not-registered"}""", settled);
    }

    [Fact]
    public async Task A_Project_that_is_not_registered_is_a_404_problem_and_no_Signal_is_appended()
    {
        await using var host = await StartAsync();
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Post, "/api/projects/start?name=acme/shop", null, Token);

        Assert.Equal(HttpStatusCode.NotFound, answer.Status);
        Assert.Equal("urn:hephaestus:problem:project-not-registered", answer.Problem!.Type);
        Assert.Empty(await host.App.Services.GetRequiredService<ISignalInbox>().ReadAsync(0, 10, Token));
    }

    [Theory]
    [InlineData("start", "")]
    [InlineData("stop", "not-a-name")]
    [InlineData("start", "acme/shop/extra")]
    public async Task A_name_that_is_not_owner_slash_repo_is_a_400_problem(string verb, string name)
    {
        await using var host = await StartAsync();
        using var client = ClientFor(host);

        var answer = await client.SendAsync(HttpMethod.Post, $"/api/projects/{verb}?name={Uri.EscapeDataString(name)}", null, Token);

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal("urn:hephaestus:problem:project-name-invalid", answer.Problem!.Type);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("99999")]
    public async Task A_Signal_that_is_not_there_is_a_404_problem_in_the_language_of_the_request(string id)
    {
        await using var host = await StartAsync();
        using var client = ClientFor(host, Language.Polish);

        var answer = await client.SendAsync(HttpMethod.Get, $"/api/signals/{id}", null, Token);

        Assert.Equal(HttpStatusCode.NotFound, answer.Status);
        Assert.Equal("urn:hephaestus:problem:signal-not-found", answer.Problem!.Type);
        Assert.Equal("Nie znaleziono sygnału", answer.Problem.Title);
        Assert.Equal($"Nie ma sygnału {id}.", answer.Problem.Detail);
    }

    [Fact]
    public async Task Starting_and_stopping_are_mapped_only_on_the_control_socket_as_posts()
    {
        await using var host = await StartAsync();
        await RegisterAsync(host);
        using var client = ClientFor(host);

        var get = await client.SendAsync(HttpMethod.Get, "/api/projects/start?name=acme/shop", null, Token);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.Status);
    }
}
