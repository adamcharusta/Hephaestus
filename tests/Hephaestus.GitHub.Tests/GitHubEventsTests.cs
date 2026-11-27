using System.Text.Json;
using Hephaestus.Core.Signals;

namespace Hephaestus.GitHub.Tests;

/// <summary>AD-2, AD-15: GitHub's event data becomes Signals, the actor is the one GitHub names, and data that is not an event is skipped, not trusted.</summary>
public class GitHubEventsTests
{
    private static readonly DateTimeOffset Watched = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Theory]
    [InlineData("""{"id":1,"event":"labeled","created_at":"2026-11-27T12:00:00Z","actor":{"id":5,"login":"a"},"label":{"name":"x"},"issue":{"number":3}}""", SignalKind.Labeled)]
    [InlineData("""{"id":1,"event":"unlabeled","created_at":"2026-11-27T12:00:00Z","actor":{"id":5,"login":"a"},"label":{"name":"x"},"issue":{"number":3}}""", SignalKind.Unlabeled)]
    [InlineData("""{"id":1,"event":"closed","created_at":"2026-11-27T12:00:00Z","actor":{"id":5,"login":"a"},"issue":{"number":3}}""", SignalKind.Closed)]
    [InlineData("""{"id":1,"event":"reopened","created_at":"2026-11-27T12:00:00Z","actor":{"id":5,"login":"a"},"issue":{"number":3}}""", SignalKind.Reopened)]
    public void The_four_issue_events_are_Signals_of_their_kind(string json, SignalKind kind)
    {
        var read = GitHubEvents.ReadIssueEvent(Json(json), 42)!;

        Assert.Equal(1, read.Id);
        Assert.Equal((kind, 42L, 3, 5L, "a", "issue-event:1"), (read.Signal!.Kind, read.Signal.RepositoryId, read.Signal.IssueNumber, read.Signal.ActorId, read.Signal.ActorLogin, read.Signal.SourceEventId));
    }

    [Fact]
    public void The_actor_is_what_GitHub_names_and_never_what_the_label_or_anything_else_says()
    {
        var read = GitHubEvents.ReadIssueEvent(Json("""{"id":9,"event":"labeled","created_at":"2026-11-27T12:00:00Z","actor":{"id":5,"login":"mallory"},"label":{"name":"hephaestus"},"issue":{"number":3,"user":{"id":1,"login":"owner"},"body":"actor: 1"}}"""), 1)!;

        Assert.Equal(5, read.Signal!.ActorId);
    }

    [Theory]
    [InlineData("""{"id":1,"event":"assigned","created_at":"2026-11-27T12:00:00Z","actor":{"id":5},"issue":{"number":3}}""")]
    [InlineData("""{"id":1,"event":"labeled","created_at":"2026-11-27T12:00:00Z","actor":{"id":5},"issue":{"number":3,"pull_request":{}}}""")]
    [InlineData("""{"id":1,"event":"labeled","created_at":"2026-11-27T12:00:00Z","actor":{"id":5},"issue":{"number":3}}""")]
    [InlineData("""{"id":1,"event":"labeled","created_at":"2026-11-27T12:00:00Z","actor":{"id":5},"label":{"name":7},"issue":{"number":3}}""")]
    [InlineData("""{"id":1,"event":"closed","created_at":"2026-11-27T12:00:00Z","actor":{"id":5}}""")]
    [InlineData("""{"id":1,"event":"closed","created_at":"2026-11-27T12:00:00Z","actor":{"id":5},"issue":{"number":"3"}}""")]
    [InlineData("""{"id":1,"created_at":"2026-11-27T12:00:00Z","actor":{"id":5},"issue":{"number":3}}""")]
    [InlineData("""{"id":1,"event":"closed","created_at":"2026-11-27T12:00:00Z","actor":{"id":5},"issue":{"number":3000000000}}""")]
    [InlineData("""{"id":1,"event":"closed","created_at":"2026-11-27T12:00:00Z","actor":{"id":5},"issue":{"number":0}}""")]
    [InlineData("""{"id":1,"event":"closed","created_at":"2026-11-27T12:00:00Z","actor":{"id":5},"issue":{"number":-4}}""")]
    public void An_event_that_is_not_one_of_the_four_or_is_missing_what_a_Signal_needs_is_read_without_a_Signal_so_the_cursor_passes_it(string json)
    {
        var read = GitHubEvents.ReadIssueEvent(Json(json), 1)!;

        Assert.Equal(1, read.Id);
        Assert.Null(read.Signal);
    }

    [Theory]
    [InlineData("""{"event":"closed","created_at":"2026-11-27T12:00:00Z"}""")]
    [InlineData("""{"id":"1","event":"closed","created_at":"2026-11-27T12:00:00Z"}""")]
    [InlineData("""{"id":1,"event":"closed"}""")]
    [InlineData("""{"id":1,"event":"closed","created_at":"yesterday"}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""7""")]
    public void Something_that_is_not_an_event_at_all_is_skipped(string json)
    {
        Assert.Null(GitHubEvents.ReadIssueEvent(Json(json), 1));
    }

    [Fact]
    public void An_event_whose_actor_is_a_deleted_account_names_no_actor()
    {
        var read = GitHubEvents.ReadIssueEvent(Json("""{"id":1,"event":"closed","created_at":"2026-11-27T12:00:00Z","actor":null,"issue":{"number":3}}"""), 1)!;

        Assert.Null(read.Signal!.ActorId);
        Assert.Null(read.Signal.ActorLogin);
    }

    [Fact]
    public void A_comment_that_was_never_edited_is_one_creation_with_its_author_as_the_actor()
    {
        var read = GitHubEvents.ReadComment(Json("""{"id":9,"user":{"id":5,"login":"a"},"created_at":"2026-11-27T12:30:00Z","updated_at":"2026-11-27T12:30:00Z","issue_url":"https://api.github.com/repos/o/r/issues/12"}"""), 42, Watched)!;

        var signal = Assert.Single(read.Signals);
        Assert.Equal((SignalKind.CommentCreated, "comment:9:created", 12, 5L, 9L, 5L), (signal.Kind, signal.SourceEventId, signal.IssueNumber, signal.ActorId, signal.CommentId, signal.CommentAuthorId));
        Assert.Equal(DateTimeOffset.Parse("2026-11-27T12:30:00Z", System.Globalization.CultureInfo.InvariantCulture), read.UpdatedAt);
    }

    [Fact]
    public void A_comment_whose_author_is_gone_has_a_creation_with_no_actor()
    {
        var read = GitHubEvents.ReadComment(Json("""{"id":9,"user":null,"created_at":"2026-11-27T12:30:00Z","updated_at":"2026-11-27T12:30:00Z","issue_url":"https://api.github.com/repos/o/r/issues/12"}"""), 42, Watched)!;

        var signal = Assert.Single(read.Signals);
        Assert.Null(signal.ActorId);
        Assert.Null(signal.CommentAuthorId);
    }

    [Theory]
    [InlineData("""{"id":9,"created_at":"2026-11-27T12:30:00Z","updated_at":"2026-11-27T12:30:00Z"}""")]
    [InlineData("""{"id":9,"created_at":"2026-11-27T12:30:00Z","updated_at":"2026-11-27T12:30:00Z","issue_url":"https://api.github.com/repos/o/r/issues/x"}""")]
    [InlineData("""{"id":9,"created_at":"2026-11-27T12:30:00Z","updated_at":"2026-11-27T12:30:00Z","issue_url":"https://api.github.com/repos/o/r/issues/0"}""")]
    [InlineData("""{"id":9,"created_at":"2026-11-27T12:30:00Z","issue_url":"https://api.github.com/repos/o/r/issues/3"}""")]
    [InlineData("""{"created_at":"2026-11-27T12:30:00Z","updated_at":"2026-11-27T12:30:00Z","issue_url":"https://api.github.com/repos/o/r/issues/3"}""")]
    [InlineData("""[]""")]
    public void Something_that_is_not_a_comment_is_skipped(string json)
    {
        Assert.Null(GitHubEvents.ReadComment(Json(json), 1, Watched));
    }

    [Fact]
    public void A_comment_from_before_the_Project_was_watched_is_not_a_creation_and_one_from_that_second_is()
    {
        var before = GitHubEvents.ReadComment(Json("""{"id":9,"user":{"id":5},"created_at":"2026-11-27T11:59:59Z","updated_at":"2026-11-27T12:00:05Z","issue_url":"https://x/issues/1"}"""), 1, Watched)!;
        var exactly = GitHubEvents.ReadComment(Json("""{"id":10,"user":{"id":5},"created_at":"2026-11-27T12:00:00Z","updated_at":"2026-11-27T12:00:00Z","issue_url":"https://x/issues/1"}"""), 1, Watched)!;

        Assert.Equal([SignalKind.CommentEdited], before.Signals.Select(s => s.Kind));
        Assert.Equal([SignalKind.CommentCreated], exactly.Signals.Select(s => s.Kind));
    }

    [Fact]
    public void A_time_is_written_and_read_as_GitHub_writes_it()
    {
        var instant = new DateTimeOffset(2026, 11, 27, 12, 0, 5, 700, TimeSpan.FromHours(2));

        Assert.Equal("2026-11-27T10:00:05Z", GitHubEvents.Instant(instant));
        Assert.True(GitHubEvents.TryParseInstant("2026-11-27T12:00:00.1234567Z", out var parsed));
        Assert.Equal(TimeSpan.Zero, parsed.Offset);
        Assert.False(GitHubEvents.TryParseInstant("later", out _));
        Assert.False(GitHubEvents.TryParseInstant(null, out _));
    }

    [Fact]
    public void An_edit_from_before_the_Project_was_watched_is_no_Signal_though_the_comment_is_read()
    {
        var read = GitHubEvents.ReadComment(Json("""{"id":9,"user":{"id":5},"created_at":"2026-11-27T11:00:00Z","updated_at":"2026-11-27T11:59:59Z","issue_url":"https://x/issues/1"}"""), 1, Watched)!;

        Assert.Empty(read.Signals);
        Assert.Equal(DateTimeOffset.Parse("2026-11-27T11:59:59Z", System.Globalization.CultureInfo.InvariantCulture), read.UpdatedAt);
    }
}
