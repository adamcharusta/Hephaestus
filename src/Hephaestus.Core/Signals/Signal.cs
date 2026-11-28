using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Signals;

/// <summary>
/// What happened that can matter to a Task (AD-2): an actor-attributed event on GitHub, never an inference from the labels or the
/// open or closed state an issue has now, or the end of a step container, which only the runtime can report (AD-18). Pull request events
/// join this set with the stories that act on them.
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

    /// <summary>A step container ended; the runtime appends it once for each step run (AD-18).</summary>
    StepExited,

    /// <summary>The Owner asked, through the control API, to start work on a Project outside its Schedule (FR-16).</summary>
    StartRequested,

    /// <summary>The Owner asked, through the control API, to stop working a Project's window.</summary>
    StopRequested,
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
        SignalKind.StepExited => "step-exited",
        SignalKind.StartRequested => "start-requested",
        SignalKind.StopRequested => "stop-requested",
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
/// <param name="IssueTitle">For a <see cref="SignalKind.Labeled"/> event, the issue's title as GitHub's event data showed it when the poller read the event; null for the other kinds and for a Signal that carries none.</param>
/// <param name="IssueBody">For a <see cref="SignalKind.Labeled"/> event, the issue's body as it was shown with <paramref name="IssueTitle"/>; empty when the issue has none.</param>
/// <param name="CommentBody">For a <see cref="SignalKind.CommentCreated"/> event, the comment's text as GitHub showed it when the poller read the event; empty when the comment has none; null for the other kinds. Whether it is used is decided later, by the author's id (AD-15).</param>
/// <param name="StepRunId">For a <see cref="SignalKind.StepExited"/> Signal, the step run whose container ended; null for the other kinds.</param>
/// <param name="ExitCode">For a <see cref="SignalKind.StepExited"/> Signal, the exit code of the container's process; null when none is known (<see cref="StepExitCause.Lost"/>).</param>
/// <param name="Cause">For a <see cref="SignalKind.StepExited"/> Signal, why the container ended, as the runtime saw it.</param>
/// <param name="RefusedHosts">For a <see cref="SignalKind.StepExited"/> Signal, the host names the egress proxy refused during the step run, each once, in the order they were first refused; null when it refused none and for the other kinds (AD-7, AD-18).</param>
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
    DateTimeOffset OccurredAt,
    string? IssueTitle = null,
    string? IssueBody = null,
    string? CommentBody = null,
    Guid? StepRunId = null,
    int? ExitCode = null,
    StepExitCause? Cause = null,
    IReadOnlyList<string>? RefusedHosts = null)
{
    /// <summary>The one source of the Signals the GitHub gateway's poller appends.</summary>
    public const string GitHubSource = "github";

    /// <summary>The source of the Signals the runtime appends.</summary>
    public const string RuntimeSource = "runtime";

    /// <summary>The source of the Signals the control API appends: the Owner's own requests, which reach the socket only through the host's own container (AD-17).</summary>
    public const string ControlSource = "control";

    /// <summary>The Owner's request to start work on a Project. Each request is its own event, so two requests are two Signals; the loop opens one window for both (FR-16).</summary>
    public static Signal Start(long repositoryId, Guid requestId, DateTimeOffset occurredAt) => Request(SignalKind.StartRequested, repositoryId, requestId, occurredAt);

    /// <summary>The Owner's request to stop working a Project's window.</summary>
    public static Signal Stop(long repositoryId, Guid requestId, DateTimeOffset occurredAt) => Request(SignalKind.StopRequested, repositoryId, requestId, occurredAt);

    private static Signal Request(SignalKind kind, long repositoryId, Guid requestId, DateTimeOffset occurredAt) =>
        new(ControlSource, requestId.ToString("D"), kind, repositoryId, 0, null, null, null, null, null, occurredAt);

    /// <summary>
    /// The one Signal that reports the end of a step run's container (AD-18). Its source event id is the step run, so a second append, which a
    /// restart can make, is the no-op the inbox makes of every duplicate: the loop hears of a step's end exactly once. It concerns no
    /// repository or issue by itself; the loop finds the Task through the step run.
    /// </summary>
    public static Signal StepExited(Guid stepRunId, int? exitCode, StepExitCause cause, DateTimeOffset occurredAt, IReadOnlyList<string>? refusedHosts = null) =>
        new(RuntimeSource, stepRunId.ToString("D"), SignalKind.StepExited, 0, 0, null, null, null, null, null, occurredAt, StepRunId: stepRunId, ExitCode: exitCode, Cause: cause, RefusedHosts: refusedHosts is { Count: > 0 } ? refusedHosts : null);
}

/// <summary>
/// A Signal as the inbox holds it: the position the database gave it, which is the inbox order, and what became of it. Only the
/// orchestrator loop moves a Signal out of <see cref="SignalStatus.Pending"/> (AD-1).
/// </summary>
/// <param name="Status">Pending until the loop has applied it in one transaction, then applied or rejected.</param>
/// <param name="Reason">Why a rejected Signal was rejected, as one of the <see cref="SignalRejections"/>; for an applied one, a note from <see cref="SignalNotes"/> when it changed nothing for a reason worth saying; null otherwise.</param>
/// <param name="TaskId">The Task the Signal created or concerned, when the loop found one; null otherwise.</param>
public sealed record StoredSignal(long Sequence, Signal Signal, SignalStatus Status = SignalStatus.Pending, string? Reason = null, Guid? TaskId = null);
