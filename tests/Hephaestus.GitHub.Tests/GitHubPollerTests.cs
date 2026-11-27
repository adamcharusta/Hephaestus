using System.Net;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
using Hephaestus.Core.Signals;

namespace Hephaestus.GitHub.Tests;

/// <summary>Story 2.1, AD-1, AD-2, AD-4, AD-15, AR-43: the poller appends one actor-attributed Signal per event, appends first and moves the cursor second, and sends conditional requests.</summary>
public class GitHubPollerTests
{
    private const string IssueEvents = "/issues/events";
    private const string Comments = "/issues/comments";

    [Fact]
    public async Task A_label_an_unlabel_a_close_and_a_reopen_are_one_Signal_each_in_the_order_GitHub_numbered_them_with_the_actors_numeric_id()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] =
        [
            PollerFixture.Event(103, "closed", 7, actor: 31, at: "2026-11-27T12:10:03Z"),
            PollerFixture.Event(101, "labeled", 7, actor: 29, at: "2026-11-27T12:10:01Z", label: "hephaestus"),
            PollerFixture.Event(104, "reopened", 7, actor: 31, at: "2026-11-27T12:10:04Z"),
            PollerFixture.Event(102, "unlabeled", 8, actor: 30, at: "2026-11-27T12:10:02Z", label: "hephaestus"),
        ];

        var report = await fixture.PollAsync();

        Assert.Equal((1, 4), (report.ProjectsRead, report.SignalsAppended));
        Assert.Equal(
            [
                ("issue-event:101", SignalKind.Labeled, 7, 29L, "hephaestus"),
                ("issue-event:102", SignalKind.Unlabeled, 8, 30L, "hephaestus"),
                ("issue-event:103", SignalKind.Closed, 7, 31L, null),
                ("issue-event:104", SignalKind.Reopened, 7, 31L, null),
            ],
            fixture.Inbox.Signals.Select(s => (s.SourceEventId, s.Kind, s.IssueNumber, s.ActorId!.Value, s.Label)));
        Assert.All(fixture.Inbox.Signals, s => Assert.Equal(("github", PollerFixture.RepositoryId), (s.Source, s.RepositoryId)));
        Assert.Equal("user29", fixture.Inbox.Signals.First().ActorLogin);
        Assert.Equal(DateTimeOffset.Parse("2026-11-27T12:10:01Z", System.Globalization.CultureInfo.InvariantCulture), fixture.Inbox.Signals.First().OccurredAt);
        Assert.Equal("104", fixture.Cursors.Stored[(1, PollStreams.IssueEvents)].Position);
    }

    [Fact]
    public async Task A_comment_that_was_created_is_one_Signal_with_its_author_as_the_actor()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.Comments[1] = [PollerFixture.Comment(900, 7, author: 29, created: "2026-11-27T12:20:00Z", updated: "2026-11-27T12:20:00Z")];

        await fixture.PollAsync();

        var signal = Assert.Single(fixture.Inbox.Signals);
        Assert.Equal(("comment:900:created", SignalKind.CommentCreated, 7, 29L, 900L, 29L), (signal.SourceEventId, signal.Kind, signal.IssueNumber, signal.ActorId, signal.CommentId, signal.CommentAuthorId));
        Assert.Equal("2026-11-27T12:20:00Z", fixture.Cursors.Stored[(1, PollStreams.Comments)].Position);
    }

    [Fact]
    public async Task A_comment_seen_already_edited_still_gives_its_creation_and_the_edit_names_no_actor_because_the_event_data_holds_the_author_only()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.Comments[1] = [PollerFixture.Comment(900, 7, author: 29, created: "2026-11-27T12:20:00Z", updated: "2026-11-27T12:25:00Z")];

        await fixture.PollAsync();

        Assert.Collection(
            fixture.Inbox.Signals,
            created => Assert.Equal(("comment:900:created", SignalKind.CommentCreated, 29L), (created.SourceEventId, created.Kind, created.ActorId)),
            edited =>
            {
                Assert.Equal(("comment:900:edited:2026-11-27T12:25:00Z", SignalKind.CommentEdited), (edited.SourceEventId, edited.Kind));
                Assert.Null(edited.ActorId);
                Assert.Equal(29L, edited.CommentAuthorId);
            });
    }

    [Fact]
    public async Task Another_edit_of_the_same_comment_is_a_new_Signal_and_the_same_edit_is_not()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.Comments[1] = [PollerFixture.Comment(900, 7, 29, "2026-11-27T12:20:00Z", "2026-11-27T12:25:00Z")];
        await fixture.PollAsync();
        fixture.GitHub.Comments[1] = [PollerFixture.Comment(900, 7, 29, "2026-11-27T12:20:00Z", "2026-11-27T12:26:00Z")];

        var again = await fixture.PollAsync();

        Assert.Equal(1, again.SignalsAppended);
        Assert.Equal(
            ["comment:900:created", "comment:900:edited:2026-11-27T12:25:00Z", "comment:900:edited:2026-11-27T12:26:00Z"],
            fixture.Inbox.Signals.Select(s => s.SourceEventId));
    }

    [Fact]
    public async Task A_comment_made_before_the_Project_was_watched_gives_only_its_edit()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.Comments[1] = [PollerFixture.Comment(800, 7, 29, "2026-11-20T09:00:00Z", "2026-11-27T12:30:00Z")];

        await fixture.PollAsync();

        Assert.Equal(["comment:800:edited:2026-11-27T12:30:00Z"], fixture.Inbox.Signals.Select(s => s.SourceEventId));
    }

    [Fact]
    public async Task Events_the_poller_sees_again_are_appended_once()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];
        await fixture.PollAsync();
        fixture.Cursors.Stored.Clear();

        var again = await fixture.PollAsync();

        Assert.Equal(0, again.SignalsAppended);
        Assert.Single(fixture.Inbox.Signals);
    }

    [Fact]
    public async Task A_crash_after_the_append_and_before_the_cursor_moved_loses_nothing_and_appends_nothing_twice()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus"), PollerFixture.Event(102, "closed", 7, 29)];
        fixture.GitHub.Comments[1] = [PollerFixture.Comment(900, 7, 29, "2026-11-27T12:20:00Z", "2026-11-27T12:20:00Z")];
        fixture.Cursors.Fail = new InvalidOperationException("the process ended here");

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.PollAsync());

        Assert.Equal(2, fixture.Inbox.Stored.Count);
        Assert.Empty(fixture.Cursors.Stored);

        fixture.Cursors.Fail = null;
        var restarted = await fixture.PollAsync();

        Assert.Equal(1, restarted.SignalsAppended);
        Assert.Equal(["issue-event:101", "issue-event:102", "comment:900:created"], fixture.Inbox.Signals.Select(s => s.SourceEventId));
        Assert.Equal("102", fixture.Cursors.Stored[(1, PollStreams.IssueEvents)].Position);
    }

    [Fact]
    public async Task The_cursor_moves_after_the_append_never_before()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];
        fixture.Inbox.Fail = new InvalidOperationException("the database is full");

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.PollAsync());

        Assert.DoesNotContain(fixture.Cursors.Stored, c => c.Key.Stream != PollStreams.Since);
    }

    [Fact]
    public async Task When_nothing_changed_the_request_is_conditional_a_304_leaves_the_cursor_as_it_was_and_nothing_is_appended()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];
        fixture.GitHub.Comments[1] = [PollerFixture.Comment(900, 7, 29, "2026-11-27T12:20:00Z", "2026-11-27T12:20:00Z")];
        await fixture.PollAsync();
        var cursors = fixture.Cursors.Stored.ToDictionary(c => c.Key, c => c.Value);

        // A second pass: the comments cursor settles on its own one-second overlap, after which the answer is 304 each time.
        await fixture.PollAsync();
        fixture.GitHub.ListRequests.Clear();
        var appends = fixture.Inbox.Appends;
        var quiet = await fixture.PollAsync();

        Assert.Equal((1, 0), (quiet.ProjectsRead, quiet.SignalsAppended));
        Assert.All(fixture.GitHub.ListRequests, r => Assert.NotNull(r.IfNoneMatch));
        Assert.Equal(2, fixture.GitHub.ListRequests.Count);
        Assert.Equal(cursors[(1, PollStreams.IssueEvents)], fixture.Cursors.Stored[(1, PollStreams.IssueEvents)]);
        Assert.Equal(cursors[(1, PollStreams.Comments)].Position, fixture.Cursors.Stored[(1, PollStreams.Comments)].Position);
        Assert.NotNull(fixture.Cursors.Stored[(1, PollStreams.IssueEvents)].ETag);
        Assert.Equal(appends, fixture.Inbox.Appends);
    }

    [Fact]
    public async Task A_new_event_after_a_quiet_cycle_is_read_from_the_first_page_only_and_the_old_ones_are_not_appended_again()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];
        await fixture.PollAsync();
        fixture.GitHub.IssueEvents[1].Add(PollerFixture.Event(102, "closed", 7, 29));
        fixture.GitHub.ListRequests.Clear();

        var report = await fixture.PollAsync();

        Assert.Equal(1, report.SignalsAppended);
        Assert.Equal(1, fixture.ListRequestsFor(IssueEvents));
        Assert.Equal(["issue-event:101", "issue-event:102"], fixture.Inbox.Signals.Select(s => s.SourceEventId));
    }

    [Fact]
    public async Task Pull_request_events_and_other_events_give_no_Signal_but_the_cursor_passes_them()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] =
        [
            PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus", pullRequest: true),
            PollerFixture.Event(102, "assigned", 7, 29),
            PollerFixture.Event(103, "merged", 7, 29),
            ("104".Length + 100L, """{"id":204,"event":"labeled","created_at":"2026-11-27T12:10:00Z","issue":{"number":7}}"""),
        ];

        var report = await fixture.PollAsync();

        Assert.Equal(0, report.SignalsAppended);
        Assert.Empty(fixture.Inbox.Stored);
        Assert.Equal("204", fixture.Cursors.Stored[(1, PollStreams.IssueEvents)].Position);
    }

    [Fact]
    public async Task An_event_with_no_actor_is_a_Signal_with_no_actor_which_is_never_a_Trusted_User()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "closed", 7, actor: null)];

        await fixture.PollAsync();

        var signal = Assert.Single(fixture.Inbox.Signals);
        Assert.Null(signal.ActorId);
        Assert.Null(signal.ActorLogin);
    }

    [Fact]
    public async Task A_new_Project_reads_from_the_time_it_was_registered_not_from_the_beginning_of_the_repository()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] =
        [
            PollerFixture.Event(50, "labeled", 3, 29, at: "2026-11-01T08:00:00Z", label: "hephaestus"),
            PollerFixture.Event(51, "labeled", 4, 29, at: "2026-11-27T11:59:59Z", label: "hephaestus"),
            PollerFixture.Event(52, "labeled", 5, 29, at: "2026-11-27T12:00:00Z", label: "hephaestus"),
            PollerFixture.Event(53, "labeled", 6, 29, at: "2026-11-27T12:01:00Z", label: "hephaestus"),
        ];

        await fixture.PollAsync();

        Assert.Equal(["issue-event:52", "issue-event:53"], fixture.Inbox.Signals.Select(s => s.SourceEventId));
        Assert.Equal("53", fixture.Cursors.Stored[(1, PollStreams.IssueEvents)].Position);
    }

    [Fact]
    public async Task The_moment_stored_when_the_Project_was_registered_again_decides_what_is_read()
    {
        using var fixture = new PollerFixture();
        fixture.Cursors.Stored[(1, PollStreams.Since)] = new PollCursor("2026-11-27T12:05:00.0000000Z", null);
        fixture.GitHub.IssueEvents[1] =
        [
            PollerFixture.Event(60, "labeled", 3, 29, at: "2026-11-27T12:04:00Z", label: "hephaestus"),
            PollerFixture.Event(61, "labeled", 4, 29, at: "2026-11-27T12:06:00Z", label: "hephaestus"),
        ];

        await fixture.PollAsync();

        Assert.Equal(["issue-event:61"], fixture.Inbox.Signals.Select(s => s.SourceEventId));
    }

    [Fact]
    public async Task More_events_than_a_page_are_all_read_by_following_the_pages_to_the_cursor()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] = [.. Enumerable.Range(1, 250).Select(i => PollerFixture.Event(1000 + i, "labeled", i, 29, label: "hephaestus"))];

        var report = await fixture.PollAsync();

        Assert.Equal(250, report.SignalsAppended);
        Assert.Equal(3, fixture.ListRequestsFor(IssueEvents));
        Assert.Equal(Enumerable.Range(1001, 250).Select(i => $"issue-event:{i}"), fixture.Inbox.Signals.Select(s => s.SourceEventId));
        Assert.Equal("1250", fixture.Cursors.Stored[(1, PollStreams.IssueEvents)].Position);
    }

    [Fact]
    public async Task More_comments_than_a_page_are_all_read()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.Comments[1] = [.. Enumerable.Range(0, 130).Select(i => PollerFixture.Comment(2000 + i, 7, 29, "2026-11-27T12:20:00Z", DateTimeOffset.Parse("2026-11-27T12:20:00Z", System.Globalization.CultureInfo.InvariantCulture).AddSeconds(i).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture)))];

        var report = await fixture.PollAsync();

        Assert.Equal(130 + 129, report.SignalsAppended);
        Assert.Equal(2, fixture.ListRequestsFor(Comments));
    }

    [Fact]
    public async Task A_link_to_another_host_is_not_followed_so_the_token_goes_nowhere_else()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];
        fixture.GitHub.Override = request =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith(IssueEvents, StringComparison.Ordinal))
            {
                return null;
            }

            var response = FakeGitHub.Json(HttpStatusCode.OK, """[{"id":101,"actor":{"id":29,"login":"a"},"event":"closed","created_at":"2026-11-27T12:10:00Z","issue":{"number":7}}]""");
            response.Headers.TryAddWithoutValidation("Link", "<https://evil.example/repositories/1/issues/events?page=2>; rel=\"next\"");
            return response;
        };

        var report = await fixture.PollAsync();

        Assert.Equal(1, report.SignalsAppended);
        Assert.DoesNotContain(fixture.GitHub.Requests, r => r.Path.Contains("evil", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_lists_are_read_with_an_installation_token_never_with_the_Apps_signed_token()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];

        await fixture.PollAsync();

        Assert.All(fixture.GitHub.Requests.Where(r => r.Path.StartsWith("/repositories/", StringComparison.Ordinal)), r => Assert.Equal($"Bearer {FakeGitHub.InstallationToken}", r.Authorization));
    }

    [Fact]
    public async Task A_repository_that_is_not_registered_is_never_read()
    {
        using var fixture = new PollerFixture();
        fixture.Stores.Projects.Clear();
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];

        var report = await fixture.PollAsync();

        Assert.Equal(PollCycleReport.Idle, report);
        Assert.Empty(fixture.GitHub.Requests);
        Assert.Empty(fixture.Inbox.Stored);
    }

    [Fact]
    public async Task A_removed_Project_is_never_read_while_another_Project_still_is()
    {
        using var fixture = new PollerFixture();
        fixture.Stores.Projects[0] = fixture.Stores.Projects[0] with { RemovedAt = PollerFixture.RegisteredAt.AddMinutes(1) };
        fixture.Stores.Projects.Add(new Project(2, "acme/api", "main", PollerFixture.RegisteredAt, null));
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];
        fixture.GitHub.IssueEvents[2] = [PollerFixture.Event(201, "closed", 9, 29)];

        await fixture.PollAsync();

        Assert.DoesNotContain(fixture.GitHub.ListRequests, r => r.Path.StartsWith("/repositories/1/", StringComparison.Ordinal));
        Assert.Equal([2L], fixture.Inbox.Signals.Select(s => s.RepositoryId).Distinct());
    }

    [Fact]
    public async Task A_repository_the_App_is_not_installed_on_is_reported_and_the_others_are_still_read()
    {
        using var fixture = new PollerFixture();
        fixture.Stores.Projects.Insert(0, new Project(99, "acme/gone", "main", PollerFixture.RegisteredAt, null));
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];

        var report = await fixture.PollAsync();

        Assert.Equal((1, 1), (report.ProjectsRead, report.SignalsAppended));
        Assert.Contains("acme/gone: the App is not installed on the repository.", report.Problems);
        Assert.DoesNotContain(fixture.GitHub.ListRequests, r => r.Path.StartsWith("/repositories/99/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nothing_is_polled_while_GitHub_is_not_connected()
    {
        using var fixture = new PollerFixture();
        fixture.Stores.AppId = null;

        var report = await fixture.PollAsync();

        Assert.Equal((0, 0), (report.ProjectsRead, report.SignalsAppended));
        Assert.Single(report.Problems);
        Assert.Empty(fixture.GitHub.Requests);
    }

    [Fact]
    public async Task A_stored_key_that_is_not_a_key_is_reported_and_nothing_is_sent()
    {
        using var fixture = new PollerFixture();
        fixture.Stores.Secrets[Hephaestus.Core.GitHub.GitHubAppCredential.PrivateKeySecretName] = "not a key";

        var report = await fixture.PollAsync();

        Assert.Contains("PEM", Assert.Single(report.Problems), StringComparison.Ordinal);
        Assert.Empty(fixture.GitHub.Requests);
    }

    [Fact]
    public async Task When_GitHub_cannot_be_reached_the_cycle_ends_the_cursors_stay_and_a_later_cycle_loses_nothing()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];
        fixture.GitHub.Override = request => request.RequestUri!.AbsolutePath.EndsWith(IssueEvents, StringComparison.Ordinal) ? throw new HttpRequestException("no route to host") : null;

        var down = await fixture.PollAsync();

        Assert.Equal(0, down.SignalsAppended);
        Assert.Contains("did not answer", Assert.Single(down.Problems), StringComparison.Ordinal);
        Assert.Empty(fixture.Inbox.Stored);
        Assert.Empty(fixture.Cursors.Stored);

        fixture.GitHub.Override = null;
        var back = await fixture.PollAsync();

        Assert.Equal(1, back.SignalsAppended);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task A_server_error_ends_the_cycle_without_changing_a_cursor(HttpStatusCode status)
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];
        fixture.GitHub.Override = request => request.RequestUri!.AbsolutePath.EndsWith(IssueEvents, StringComparison.Ordinal) ? FakeGitHub.Json(status, """{"message":"Server Error"}""") : null;

        var report = await fixture.PollAsync();

        Assert.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.Single(report.Problems), StringComparison.Ordinal);
        Assert.Empty(fixture.Inbox.Stored);
        Assert.Empty(fixture.Cursors.Stored);
    }

    [Fact]
    public async Task A_refusal_for_one_repository_is_reported_and_the_next_one_is_still_read()
    {
        using var fixture = new PollerFixture();
        fixture.Stores.Projects.Add(new Project(2, "acme/api", "main", PollerFixture.RegisteredAt, null));
        fixture.GitHub.IssueEvents[2] = [PollerFixture.Event(201, "closed", 9, 29)];
        fixture.GitHub.Override = request => request.RequestUri!.AbsolutePath.StartsWith("/repositories/1/", StringComparison.Ordinal) ? FakeGitHub.Json(HttpStatusCode.NotFound, """{"message":"Not Found"}""") : null;

        var report = await fixture.PollAsync();

        Assert.Equal((2, 1), (report.ProjectsRead, report.SignalsAppended));
        Assert.Contains("acme/web: GitHub answered 404.", report.Problems);
        Assert.Null(fixture.Limit.ClosedUntil);
    }

    [Fact]
    public async Task A_403_that_is_a_permission_is_not_a_rate_limit()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.Override = request => request.RequestUri!.AbsolutePath.EndsWith(IssueEvents, StringComparison.Ordinal) ? FakeGitHub.Json(HttpStatusCode.Forbidden, """{"message":"Resource not accessible by integration"}""") : null;

        var report = await fixture.PollAsync();

        Assert.Contains("acme/web: GitHub answered 403.", report.Problems);
        Assert.Null(fixture.Limit.ClosedUntil);
    }

    [Fact]
    public async Task An_installation_token_GitHub_refuses_is_asked_for_again_on_the_next_cycle()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];
        var refuse = true;
        fixture.GitHub.Override = request => refuse && request.RequestUri!.AbsolutePath.EndsWith(IssueEvents, StringComparison.Ordinal) ? FakeGitHub.Json(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""") : null;

        var refused = await fixture.PollAsync();
        refuse = false;
        var accepted = await fixture.PollAsync();

        Assert.Contains("no longer accepts", Assert.Single(refused.Problems), StringComparison.Ordinal);
        Assert.Equal(1, accepted.SignalsAppended);
        Assert.Equal(2, fixture.GitHub.Requests.Count(r => r.Path == "/app/installations"));
    }

    [Fact]
    public async Task A_credential_GitHub_refuses_ends_the_cycle_and_is_tried_again_later()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.AppStatus = HttpStatusCode.Unauthorized;
        fixture.GitHub.Override = request => request.RequestUri!.AbsolutePath == "/app/installations" ? FakeGitHub.Json(HttpStatusCode.Unauthorized, """{"message":"A JSON web token could not be decoded"}""") : null;

        var report = await fixture.PollAsync();

        Assert.Contains("answered 401", Assert.Single(report.Problems), StringComparison.Ordinal);
        Assert.Empty(fixture.Cursors.Stored);
    }

    [Fact]
    public async Task The_installations_and_their_tokens_are_kept_between_cycles_and_read_again_when_they_are_old()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];

        await fixture.PollAsync();
        await fixture.PollAsync();
        Assert.Equal(1, fixture.GitHub.Requests.Count(r => r.Path == "/app/installations"));

        fixture.Time.Now = fixture.Time.Now.AddMinutes(11);
        await fixture.PollAsync();

        Assert.Equal(2, fixture.GitHub.Requests.Count(r => r.Path == "/app/installations"));
    }

    [Fact]
    public async Task A_repository_installed_after_the_lists_were_read_is_found_but_the_lists_are_not_read_again_every_cycle_for_a_repository_that_is_not_there()
    {
        using var fixture = new PollerFixture();
        fixture.Stores.Projects.Add(new Project(77, "acme/new", "main", PollerFixture.RegisteredAt, null));

        await fixture.PollAsync();
        await fixture.PollAsync();
        var readsWithin = fixture.GitHub.Requests.Count(r => r.Path == "/app/installations");
        fixture.Time.Now = fixture.Time.Now.AddMinutes(2);
        await fixture.PollAsync();

        Assert.Equal(1, readsWithin);
        Assert.Equal(2, fixture.GitHub.Requests.Count(r => r.Path == "/app/installations"));
    }

    [Fact]
    public async Task A_minimum_interval_GitHub_returns_is_respected_for_that_repository()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.ListHeaders["X-Poll-Interval"] = "120";
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];
        await fixture.PollAsync();
        fixture.GitHub.ListRequests.Clear();

        fixture.Time.Now = fixture.Time.Now.AddSeconds(60);
        var early = await fixture.PollAsync();
        Assert.Empty(fixture.GitHub.ListRequests);
        Assert.Equal(0, early.ProjectsRead);

        fixture.Time.Now = fixture.Time.Now.AddSeconds(61);
        var due = await fixture.PollAsync();

        Assert.Equal(1, due.ProjectsRead);
        Assert.Equal(2, fixture.GitHub.ListRequests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "120", null, null, "API rate limit exceeded", 120)]
    [InlineData(HttpStatusCode.Forbidden, "30", null, null, "You have exceeded a secondary rate limit", 30)]
    [InlineData(HttpStatusCode.Forbidden, null, "0", 1795000000L, "API rate limit exceeded", -1)]
    [InlineData(HttpStatusCode.Forbidden, null, null, null, "You have exceeded a secondary rate limit. Please wait.", 60)]
    [InlineData(HttpStatusCode.TooManyRequests, null, null, null, "Slow down", 60)]
    public async Task A_rate_limit_answer_closes_the_gate_until_the_time_GitHub_states_and_no_event_is_lost(HttpStatusCode status, string? retryAfter, string? remaining, long? reset, string message, int seconds)
    {
        using var fixture = new PollerFixture();
        var start = new DateTimeOffset(2026, 11, 27, 12, 5, 0, TimeSpan.Zero);
        fixture.Time.Now = start;
        var resetAt = reset is { } r ? DateTimeOffset.FromUnixTimeSeconds(r) : (DateTimeOffset?)null;
        if (resetAt is { } moment)
        {
            fixture.Time.Now = moment.AddMinutes(-10);
            start = fixture.Time.Now;
        }

        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, at: resetAt is null ? "2026-11-27T12:10:00Z" : "2026-11-27T12:10:00Z", label: "hephaestus")];
        var limited = true;
        fixture.GitHub.Override = request => limited && request.RequestUri!.AbsolutePath.EndsWith(IssueEvents, StringComparison.Ordinal) ? PollerFixture.RateLimited(status, retryAfter, remaining, reset, message) : null;

        var report = await fixture.PollAsync();

        var expected = seconds < 0 ? resetAt!.Value.AddSeconds(1) : start.AddSeconds(seconds);
        Assert.Equal(expected, report.WaitUntil);
        Assert.Equal(expected, fixture.Limit.ClosedUntil);
        Assert.Empty(fixture.Inbox.Stored);
        Assert.Empty(fixture.Cursors.Stored);

        // Until that time nothing is sent at all.
        fixture.GitHub.Requests.Clear();
        limited = false;
        fixture.Time.Now = expected.AddSeconds(-1);
        var waiting = await fixture.PollAsync();
        Assert.Equal(expected, waiting.WaitUntil);
        Assert.Empty(fixture.GitHub.Requests);

        // After it the same events are read, and none was lost.
        fixture.Time.Now = expected.AddSeconds(1);
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, at: "2026-11-27T12:10:00Z", label: "hephaestus")];
        var resumed = await fixture.PollAsync();

        Assert.Equal(1, resumed.SignalsAppended);
        Assert.Null(fixture.Limit.ClosedUntil);
    }

    [Fact]
    public async Task A_rate_limit_answered_while_the_installations_are_listed_also_closes_the_gate()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.Override = request => request.RequestUri!.AbsolutePath == "/app/installations" ? PollerFixture.RateLimited(HttpStatusCode.TooManyRequests, retryAfter: "90") : null;

        var report = await fixture.PollAsync();

        Assert.Equal(fixture.Time.Now.AddSeconds(90), fixture.Limit.ClosedUntil);
        Assert.Equal(fixture.Limit.ClosedUntil, report.WaitUntil);
        Assert.Contains("rate limit", Assert.Single(report.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wait_GitHub_states_is_never_longer_than_two_hours()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.Override = request => request.RequestUri!.AbsolutePath.EndsWith(IssueEvents, StringComparison.Ordinal) ? PollerFixture.RateLimited(HttpStatusCode.TooManyRequests, retryAfter: "999999") : null;

        await fixture.PollAsync();

        Assert.Equal(fixture.Time.Now.AddHours(2), fixture.Limit.ClosedUntil);
    }

    /// <summary>Runs the loop until it has slept <paramref name="enough"/>, and returns how many cycles it ran.</summary>
    private static async Task<int> RunUntilSleptAsync(PollerFixture fixture, TimeSpan enough)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(PollerFixture.Token);
        var cycles = 0;
        fixture.OnWait = () =>
        {
            if (fixture.Slept >= enough)
            {
                stop.Cancel();
            }
        };

        await fixture.Poller.RunAsync(_ => cycles++, stop.Token);
        return cycles;
    }

    [Fact]
    public async Task The_loop_waits_the_default_sixty_seconds_in_slices_that_read_the_setting_again()
    {
        using var fixture = new PollerFixture();

        var cycles = await RunUntilSleptAsync(fixture, TimeSpan.FromSeconds(60));

        Assert.Equal(1, cycles);
        Assert.Equal(TimeSpan.FromSeconds(60), fixture.Slept);
        Assert.All(fixture.Waits, wait => Assert.Equal(TimeSpan.FromSeconds(15), wait));
    }

    [Theory]
    [InlineData("10", 10)]
    [InlineData("30", 30)]
    [InlineData("120", 120)]
    public async Task The_loop_waits_the_polling_interval_the_Owner_set(string setting, int seconds)
    {
        using var fixture = new PollerFixture();
        fixture.Stores.Settings["pollingInterval"] = setting;

        await RunUntilSleptAsync(fixture, TimeSpan.FromSeconds(seconds));

        Assert.Equal(TimeSpan.FromSeconds(seconds), fixture.Slept);
    }

    [Fact]
    public async Task A_change_of_the_interval_is_felt_in_the_middle_of_a_long_wait()
    {
        using var fixture = new PollerFixture();
        fixture.Stores.Settings["pollingInterval"] = "3600";
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(PollerFixture.Token);
        var cycles = 0;
        var sleptWhenTheSecondCycleRan = TimeSpan.Zero;
        fixture.OnWait = () => fixture.Stores.Settings["pollingInterval"] = "30";

        await fixture.Poller.RunAsync(
            _ =>
            {
                if (++cycles == 2)
                {
                    sleptWhenTheSecondCycleRan = fixture.Slept;
                    stop.Cancel();
                }
            },
            stop.Token);

        Assert.Equal(TimeSpan.FromSeconds(30), sleptWhenTheSecondCycleRan);
    }

    [Fact]
    public async Task The_loop_waits_longer_when_GitHub_asked_for_a_longer_wait()
    {
        using var fixture = new PollerFixture();
        fixture.Stores.Settings["pollingInterval"] = "30";
        fixture.GitHub.Override = request => request.RequestUri!.AbsolutePath == "/app/installations" ? PollerFixture.RateLimited(HttpStatusCode.TooManyRequests, retryAfter: "300") : null;

        var cycles = await RunUntilSleptAsync(fixture, TimeSpan.FromSeconds(300));

        Assert.Equal(TimeSpan.FromSeconds(300), fixture.Slept);
        Assert.Equal(1, cycles);
    }

    [Fact]
    public async Task A_fault_in_one_cycle_does_not_end_the_loop()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];
        fixture.Inbox.Fail = new InvalidOperationException("the database is locked");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(PollerFixture.Token);
        var reports = new List<PollCycleReport>();

        await fixture.Poller.RunAsync(
            report =>
            {
                reports.Add(report);
                if (reports.Count == 1)
                {
                    fixture.Inbox.Fail = null;
                }
                else
                {
                    stop.Cancel();
                }
            },
            stop.Token);

        Assert.Contains("the database is locked", Assert.Single(reports[0].Problems), StringComparison.Ordinal);
        Assert.Equal(1, reports[1].SignalsAppended);
    }

    [Fact]
    public async Task Stopping_the_host_ends_the_loop_in_the_middle_of_a_wait()
    {
        using var fixture = new PollerFixture();
        using var poller = new GitHubPoller(fixture.Stores, fixture.Stores, fixture.Inbox, fixture.Cursors, TimeProvider.System, new GitHubRateLimit(TimeProvider.System), () => fixture.GitHub);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(PollerFixture.Token);

        var running = poller.RunAsync(_ => stop.CancelAfter(TimeSpan.FromMilliseconds(50)), stop.Token);

        await running.WaitAsync(TimeSpan.FromSeconds(10), PollerFixture.Token);
        Assert.True(running.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_stop_during_a_cycle_ends_the_loop_without_a_report_of_failure()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.Hang = true;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(PollerFixture.Token);
        var reports = new List<PollCycleReport>();

        var running = fixture.Poller.RunAsync(reports.Add, stop.Token);
        await stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(10), PollerFixture.Token);

        Assert.Empty(reports);
    }

    [Fact]
    public async Task A_setting_that_cannot_be_read_falls_back_to_the_default_interval()
    {
        using var fixture = new PollerFixture();
        fixture.Stores.FailToReadSettings = new InvalidOperationException("locked");

        await RunUntilSleptAsync(fixture, TimeSpan.FromSeconds(60));

        Assert.Equal(TimeSpan.FromSeconds(60), fixture.Slept);
    }

    [Fact]
    public async Task A_garbled_answer_that_is_not_a_list_is_read_as_no_events()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.Override = request => request.RequestUri!.AbsolutePath.EndsWith(IssueEvents, StringComparison.Ordinal) ? FakeGitHub.Json(HttpStatusCode.OK, """{"message":"not a list"}""") : null;

        var report = await fixture.PollAsync();

        Assert.Equal((1, 0), (report.ProjectsRead, report.SignalsAppended));
        Assert.Equal("0", fixture.Cursors.Stored[(1, PollStreams.IssueEvents)].Position);
    }

    [Fact]
    public async Task An_element_of_the_list_that_is_not_an_event_is_skipped_and_the_others_are_read_and_a_link_that_is_not_a_next_page_ends_the_list()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.Override = request =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith(IssueEvents, StringComparison.Ordinal))
            {
                return null;
            }

            var response = FakeGitHub.Json(HttpStatusCode.OK, """[7,{"id":101,"actor":{"id":29,"login":"a"},"event":"closed","created_at":"2026-11-27T12:10:00Z","issue":{"number":7}}]""");
            response.Headers.TryAddWithoutValidation("Link", "<https://api.github.com/repositories/1/issues/events?page=1>; rel=\"prev\"");
            return response;
        };

        var report = await fixture.PollAsync();

        Assert.Equal(1, report.SignalsAppended);
        Assert.Equal(1, fixture.GitHub.Requests.Count(r => r.Path.EndsWith(IssueEvents, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task An_answer_that_is_not_JSON_is_that_Projects_trouble_and_the_others_are_still_read()
    {
        using var fixture = new PollerFixture();
        fixture.Stores.Projects.Add(new Project(2, "acme/api", "main", PollerFixture.RegisteredAt, null));
        fixture.GitHub.IssueEvents[2] = [PollerFixture.Event(201, "closed", 9, 29)];
        fixture.GitHub.Override = request => request.RequestUri!.AbsolutePath.StartsWith("/repositories/1/", StringComparison.Ordinal) ? FakeGitHub.Json(HttpStatusCode.OK, "<html>Bad gateway</html>") : null;

        var report = await fixture.PollAsync();

        Assert.Equal(1, report.SignalsAppended);
        Assert.Equal(2, report.Problems.Count(p => p == "acme/web: GitHub's answer is not JSON."));
        Assert.DoesNotContain(fixture.Cursors.Stored, c => c.Key.RepositoryId == 1);
    }

    [Fact]
    public async Task What_the_events_gave_is_counted_when_the_comments_cannot_be_read()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];
        fixture.GitHub.Override = request => request.RequestUri!.AbsolutePath.EndsWith(Comments, StringComparison.Ordinal) ? FakeGitHub.Json(HttpStatusCode.NotFound, """{"message":"Not Found"}""") : null;

        var report = await fixture.PollAsync();

        Assert.Equal(1, report.SignalsAppended);
        Assert.Contains("acme/web: GitHub answered 404.", report.Problems);
        Assert.True(fixture.Cursors.Stored.ContainsKey((1, PollStreams.IssueEvents)));
    }

    [Fact]
    public async Task A_rate_limit_a_command_met_in_the_middle_of_a_cycle_stops_the_cycle_before_the_next_request()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];
        var until = fixture.Time.Now.AddMinutes(3);
        fixture.GitHub.Override = request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(IssueEvents, StringComparison.Ordinal))
            {
                fixture.Limit.CloseUntil(until);
            }

            return null;
        };

        var report = await fixture.PollAsync();

        Assert.Equal(1, report.SignalsAppended);
        Assert.Equal(until, report.WaitUntil);
        Assert.Equal(0, fixture.ListRequestsFor(Comments));
    }

    [Fact]
    public async Task A_minimum_interval_GitHub_states_is_never_taken_to_be_longer_than_two_hours()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.ListHeaders["X-Poll-Interval"] = "2000000000";
        await fixture.PollAsync();
        fixture.GitHub.ListRequests.Clear();

        fixture.Time.Now = fixture.Time.Now.AddHours(2).AddSeconds(1);
        var report = await fixture.PollAsync();

        Assert.Equal(1, report.ProjectsRead);
        Assert.NotEmpty(fixture.GitHub.ListRequests);
    }

    [Fact]
    public async Task An_installation_GitHub_refuses_does_not_stop_the_Projects_of_the_others()
    {
        using var fixture = new PollerFixture();
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];
        fixture.GitHub.Override = request => request.RequestUri!.AbsolutePath == "/app/installations/10/access_tokens" ? FakeGitHub.Json(HttpStatusCode.Forbidden, """{"message":"Resource not accessible by integration"}""") : null;

        var report = await fixture.PollAsync();

        Assert.Equal((1, 1), (report.ProjectsRead, report.SignalsAppended));
        Assert.Empty(report.Problems);
    }

    [Fact]
    public async Task A_fault_in_a_cycle_is_handed_to_the_host_with_its_exception_so_it_can_be_logged_whole()
    {
        using var fixture = new PollerFixture();
        var fault = new InvalidOperationException("the database is locked");
        fixture.Inbox.Fail = fault;
        fixture.GitHub.IssueEvents[1] = [PollerFixture.Event(101, "labeled", 7, 29, label: "hephaestus")];
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(PollerFixture.Token);
        PollCycleReport? seen = null;

        await fixture.Poller.RunAsync(
            report =>
            {
                seen = report;
                stop.Cancel();
            },
            stop.Token);

        Assert.Same(fault, seen!.Failure);
    }
}
