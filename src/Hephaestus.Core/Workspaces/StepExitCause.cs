namespace Hephaestus.Core.Workspaces;

/// <summary>
/// Why a step container ended, as the runtime itself knows it (AD-18): what it saw, not what an Agent said. Whether a CLI that ended by itself
/// ended well is for its adapter to classify (Story 3.16).
/// </summary>
public enum StepExitCause
{
    /// <summary>The process ended by itself, whatever its exit code.</summary>
    Exited,

    /// <summary>The deadline passed while it ran, and the runtime stopped it (FR-19).</summary>
    Timeout,

    /// <summary>The kernel ended it for using more memory than its limit.</summary>
    OutOfMemory,

    /// <summary>The container was gone when the runtime looked, so no exit code is known.</summary>
    Lost,
}

public static class StepExitCauses
{
    /// <summary>The name stored and shown for a cause; never translated.</summary>
    public static string Tag(this StepExitCause cause) => cause switch
    {
        StepExitCause.Exited => "exited",
        StepExitCause.Timeout => "timeout",
        StepExitCause.OutOfMemory => "out-of-memory",
        StepExitCause.Lost => "lost",
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, null),
    };

    public static bool TryParse(string? tag, out StepExitCause cause)
    {
        foreach (var candidate in Enum.GetValues<StepExitCause>())
        {
            if (candidate.Tag() == tag)
            {
                cause = candidate;
                return true;
            }
        }

        cause = default;
        return false;
    }
}
