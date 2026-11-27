namespace Hephaestus.Core.Tasks;

/// <summary>The states of a Task; the lifecycle diagram of the architecture spine is the complete transition table (AR-31).</summary>
public enum TaskState
{
    Queued,
    Running,
    WaitingForOwner,
    WaitingForCapacity,
    PullRequestOpen,
    Failed,
    Cancelled,
    Withdrawn,
    Merged,
    Closed,
}

public static class TaskStates
{
    /// <summary>The name stored, shown, and used in the label <c>hephaestus:&lt;state&gt;</c>: kebab-case, never translated.</summary>
    public static string Tag(this TaskState state) => state switch
    {
        TaskState.Queued => "queued",
        TaskState.Running => "running",
        TaskState.WaitingForOwner => "waiting-for-owner",
        TaskState.WaitingForCapacity => "waiting-for-capacity",
        TaskState.PullRequestOpen => "pull-request-open",
        TaskState.Failed => "failed",
        TaskState.Cancelled => "cancelled",
        TaskState.Withdrawn => "withdrawn",
        TaskState.Merged => "merged",
        TaskState.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static bool TryParse(string? tag, out TaskState state)
    {
        foreach (var candidate in Enum.GetValues<TaskState>())
        {
            if (candidate.Tag() == tag)
            {
                state = candidate;
                return true;
            }
        }

        state = default;
        return false;
    }
}
