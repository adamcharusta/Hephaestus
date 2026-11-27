namespace Hephaestus.Core.Signals;

/// <summary>
/// What happened on GitHub that can matter to a Task (AD-2): an actor-attributed event, never an inference from the labels or the
/// open or closed state an issue has now. Pull request events join this set with the stories that act on them.
/// </summary>
public enum SignalKind
{
    /// <summary>A label was applied to an issue.</summary>
    Labeled,

    /// <summary>A label was removed from an issue.</summary>
    Unlabeled,

    /// <summary>An issue was closed.</summary>
    Closed,

    /// <summary>A closed issue was opened again.</summary>
    Reopened,

    /// <summary>A comment was created.</summary>
    CommentCreated,

    /// <summary>A comment was edited. Its editor is not in the event data (AD-15), so the Signal names none.</summary>
    CommentEdited,
}

public static class SignalKinds
{
    /// <summary>The name stored and shown for a kind; never translated.</summary>
    public static string Tag(this SignalKind kind) => kind switch
    {
        SignalKind.Labeled => "labeled",
        SignalKind.Unlabeled => "unlabeled",
        SignalKind.Closed => "closed",
        SignalKind.Reopened => "reopened",
        SignalKind.CommentCreated => "comment-created",
        SignalKind.CommentEdited => "comment-edited",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static bool TryParse(string? tag, out SignalKind kind)
    {
        foreach (var candidate in Enum.GetValues<SignalKind>())
        {
            if (candidate.Tag() == tag)
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }
}

/// <summary>
/// One event as the poller appends it to the persisted inbox (AD-1). Trust is not decided here: the Signal carries who acted, taken
/// from GitHub's own event data, and one admission function in Core decides later whether that id is a Trusted User (AD-15).
/// </summary>
/// <param name="Source">Where the event came from; with <paramref name="SourceEventId"/> it is unique, and a second append of the same pair is a no-op.</param>
/// <param name="SourceEventId">The event's id at its source: GitHub's event id, or a comment's id with what makes this event of it.</param>
/// <param name="RepositoryId">GitHub's numeric id of the repository, which is the identity of the Project.</param>
/// <param name="IssueNumber">The number of the issue (or of the pull request, for a comment on one).</param>
/// <param name="ActorId">GitHub's numeric id of the user who acted; null when GitHub names none, for example a deleted account, or, for an edit of a comment, when the editor cannot be established. Such an actor is never a Trusted User (AD-15).</param>
/// <param name="ActorLogin">The actor's login at the time, for display only.</param>
/// <param name="Label">The name of the label of a <see cref="SignalKind.Labeled"/> or <see cref="SignalKind.Unlabeled"/> event.</param>
/// <param name="CommentId">GitHub's id of the comment of a comment event.</param>
/// <param name="CommentAuthorId">The numeric id of the comment's author, which for an edit is not the actor.</param>
/// <param name="OccurredAt">When GitHub says the event happened.</param>
public sealed record Signal(
    string Source,
    string SourceEventId,
    SignalKind Kind,
    long RepositoryId,
    int IssueNumber,
    long? ActorId,
    string? ActorLogin,
    string? Label,
    long? CommentId,
    long? CommentAuthorId,
    DateTimeOffset OccurredAt)
{
    /// <summary>The one source of the Signals the GitHub gateway's poller appends.</summary>
    public const string GitHubSource = "github";
}

/// <summary>A Signal as the inbox holds it, with the position the database gave it: inbox order is this sequence.</summary>
public sealed record StoredSignal(long Sequence, Signal Signal);
