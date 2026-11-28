namespace Hephaestus.Core.Tasks;

/// <summary>
/// A Task: one Project and one issue number for life (AD-2). Only the orchestrator loop writes it (AD-1). The branch is derived once,
/// when the Task is created, and never changes.
/// </summary>
/// <param name="RepositoryId">GitHub's numeric id of the Project's repository.</param>
/// <param name="Branch">The Task branch, <c>hephaestus/&lt;issue-number&gt;-&lt;slug&gt;</c> (see <see cref="BranchName"/>).</param>
/// <param name="RunningTime">The time the Task spent in <c>Running</c> before it last entered it, which the database adds up whenever the Task leaves that state (NFR-5).</param>
/// <param name="RunningSince">When the Task entered <c>Running</c>, while it is in it; null otherwise.</param>
public sealed record TaskRecord(
    Guid Id,
    long RepositoryId,
    int IssueNumber,
    TaskState State,
    string Branch,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    TimeSpan RunningTime = default,
    DateTimeOffset? RunningSince = null)
{
    /// <summary>The Task's time in <c>Running</c> at <paramref name="now"/>: what it spent before, and, while it runs, the time since it entered the state.</summary>
    public TimeSpan TimeInRunning(DateTimeOffset now) =>
        State == TaskState.Running && RunningSince is { } since && now > since ? RunningTime + (now - since) : RunningTime;
}

/// <summary>
/// The issue's title and body as the labelled event carried them (AD-15), which is as GitHub showed them when the poller read the
/// event, with the hash that a later comparison uses to notice an edit. Context for an Agent is built from an admitted snapshot only.
/// </summary>
/// <param name="Hash">The hash of the title and the body, <see cref="IssueContent.Hash"/>.</param>
/// <param name="SignalSequence">The inbox sequence of the Signal that vouched for this content.</param>
public sealed record IssueSnapshot(string Title, string Body, string Hash, long SignalSequence, DateTimeOffset TakenAt);
