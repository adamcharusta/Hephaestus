using System.Globalization;
using System.Text.Json;
using Hephaestus.Core.Signals;

namespace Hephaestus.GitHub;

/// <summary>An issue event as GitHub lists it: its id and time, which the poller's cursor is made of, and the Signal it gives, or none.</summary>
internal sealed record IssueEventRead(long Id, DateTimeOffset CreatedAt, Signal? Signal);

/// <summary>An issue comment as GitHub lists it: when it was last updated, which the poller's cursor is made of, and the Signals it gives.</summary>
internal sealed record CommentRead(DateTimeOffset UpdatedAt, IReadOnlyList<Signal> Signals);

/// <summary>
/// Reads GitHub's event data into Signals (AD-2). The actor is what GitHub's own data names, never something the content says
/// (AD-15). Nothing here decides whether an actor is trusted.
/// </summary>
internal static class GitHubEvents
{
    /// <summary>The issue events that are Signals, by the name GitHub gives them.</summary>
    private static readonly Dictionary<string, SignalKind> IssueEventKinds = new(StringComparer.Ordinal)
    {
        ["labeled"] = SignalKind.Labeled,
        ["unlabeled"] = SignalKind.Unlabeled,
        ["closed"] = SignalKind.Closed,
        ["reopened"] = SignalKind.Reopened,
    };

    /// <summary>
    /// Reads one element of <c>GET /repositories/{id}/issues/events</c>. An element that is not an event (no id or no time) is null.
    /// An event of another kind, or one that concerns a pull request, is read without a Signal, so that the cursor still passes it.
    /// </summary>
    public static IssueEventRead? ReadIssueEvent(JsonElement element, long repositoryId)
    {
        if (element.ValueKind != JsonValueKind.Object || Long(element, "id") is not { } id || Time(element, "created_at") is not { } createdAt)
        {
            return null;
        }

        var issue = Object(element, "issue");
        var number = issue is null ? null : Long(issue.Value, "number");
        var isPullRequest = issue is not null && Object(issue.Value, "pull_request") is not null;
        if (String(element, "event") is not { } name || !IssueEventKinds.TryGetValue(name, out var kind) || number is not (> 0 and <= int.MaxValue) || isPullRequest)
        {
            return new IssueEventRead(id, createdAt, Signal: null);
        }

        var label = kind is SignalKind.Labeled or SignalKind.Unlabeled ? String(Object(element, "label"), "name") : null;
        if (kind is SignalKind.Labeled or SignalKind.Unlabeled && label is null)
        {
            return new IssueEventRead(id, createdAt, Signal: null);
        }

        var actor = Object(element, "actor");

        // A labelled event carries the issue as GitHub showed it when the event was read. Which content a Trusted User vouched for is
        // decided later, by the loop, from this and the actor; nothing is decided here (AD-15).
        var title = kind == SignalKind.Labeled ? String(issue, "title") : null;
        var body = kind == SignalKind.Labeled && title is not null ? String(issue, "body") ?? string.Empty : null;
        var signal = new Signal(
            Signal.GitHubSource,
            $"issue-event:{id.ToString(CultureInfo.InvariantCulture)}",
            kind,
            repositoryId,
            (int)number,
            Long(actor, "id"),
            String(actor, "login"),
            label,
            CommentId: null,
            CommentAuthorId: null,
            createdAt,
            title,
            body);
        return new IssueEventRead(id, createdAt, signal);
    }

    /// <summary>
    /// Reads one element of <c>GET /repositories/{id}/issues/comments</c>. A comment made since <paramref name="watchedSince"/> gives a
    /// creation Signal even when it has been edited already, so that a creation is never lost; one that was edited gives an edit
    /// Signal for that update, when the update came after the Project was watched. The event data holds the comment's author and not who edited it, so an edit names no actor (AD-15).
    /// </summary>
    public static CommentRead? ReadComment(JsonElement element, long repositoryId, DateTimeOffset watchedSince)
    {
        if (element.ValueKind != JsonValueKind.Object
            || Long(element, "id") is not { } id
            || Time(element, "created_at") is not { } createdAt
            || Time(element, "updated_at") is not { } updatedAt
            || IssueNumberOf(String(element, "issue_url")) is not { } issueNumber)
        {
            return null;
        }

        var author = Object(element, "user");
        var authorId = Long(author, "id");
        var authorLogin = String(author, "login");
        var signals = new List<Signal>(2);
        if (createdAt >= watchedSince)
        {
            signals.Add(new Signal(
                Signal.GitHubSource, $"comment:{id.ToString(CultureInfo.InvariantCulture)}:created", SignalKind.CommentCreated, repositoryId, issueNumber, authorId, authorLogin, Label: null, id, authorId, createdAt));
        }

        if (updatedAt > createdAt && updatedAt >= watchedSince)
        {
            signals.Add(new Signal(
                Signal.GitHubSource, $"comment:{id.ToString(CultureInfo.InvariantCulture)}:edited:{Instant(updatedAt)}", SignalKind.CommentEdited, repositoryId, issueNumber, ActorId: null, ActorLogin: null, Label: null, id, authorId, updatedAt));
        }

        return new CommentRead(updatedAt, signals);
    }

    /// <summary>A time as GitHub writes it: UTC, to the second.</summary>
    public static string Instant(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static bool TryParseInstant(string? text, out DateTimeOffset instant) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out instant);

    private static int? IssueNumberOf(string? issueUrl)
    {
        var last = issueUrl?[(issueUrl.LastIndexOf('/') + 1)..];
        return int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;
    }

    private static JsonElement? Object(JsonElement? parent, string name) =>
        parent is { ValueKind: JsonValueKind.Object } element && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static string? String(JsonElement? parent, string name) =>
        parent is { ValueKind: JsonValueKind.Object } element && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Long(JsonElement? parent, string name) =>
        parent is { ValueKind: JsonValueKind.Object } element && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;

    private static DateTimeOffset? Time(JsonElement parent, string name) => TryParseInstant(String(parent, name), out var instant) ? instant : null;
}
