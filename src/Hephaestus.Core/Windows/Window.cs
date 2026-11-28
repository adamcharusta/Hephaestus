namespace Hephaestus.Core.Windows;

/// <summary>
/// A window is the time in which a Project's Queue is worked (FR-12, FR-16): the record of it is what lets the loop dispatch a Task of the Project (AD-1). This story opens one
/// on the Owner's request; Schedules open them in v0.2. A Project has at most one open window, which the database enforces.
/// </summary>
/// <param name="Id">The window's id.</param>
/// <param name="RepositoryId">The Project, by GitHub's repository id.</param>
/// <param name="OpenedAt">When the Signal that opened it was applied.</param>
/// <param name="ClosedAt">When it closed; null while it is open.</param>
/// <param name="End">Why it closed; null while it is open.</param>
public sealed record Window(Guid Id, long RepositoryId, DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt = null, WindowEnd? End = null)
{
    public bool IsOpen => ClosedAt is null;
}

/// <summary>Why a window closed.</summary>
public enum WindowEnd
{
    /// <summary>The Owner stopped it.</summary>
    Stopped,

    /// <summary>No Task of the Project was eligible any more: the Queue was empty (FR-16).</summary>
    Drained,
}

public static class WindowEnds
{
    /// <summary>The name stored and shown; never translated.</summary>
    public static string Tag(this WindowEnd end) => end switch
    {
        WindowEnd.Stopped => "stopped",
        WindowEnd.Drained => "drained",
        _ => throw new ArgumentOutOfRangeException(nameof(end), end, null),
    };

    public static bool TryParse(string? tag, out WindowEnd end)
    {
        foreach (var candidate in Enum.GetValues<WindowEnd>())
        {
            if (candidate.Tag() == tag)
            {
                end = candidate;
                return true;
            }
        }

        end = default;
        return false;
    }
}
