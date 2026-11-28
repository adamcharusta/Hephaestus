using Hephaestus.Core.Windows;

namespace Hephaestus.Core.Tasks;

/// <summary>
/// Which Task is dispatched, computed from recorded state and never from a queue held in memory (AD-1). Only a <c>Queued</c> Task is eligible, so a Task in <c>WaitingForOwner</c>, which has asked the Owner a
/// question, takes no part and holds no other Task back (FR-48), and a Task of a Project with no open window is not eligible, because dispatching a Task that has not started needs an open window. At most one
/// Task runs in the whole installation (FR-14). Until the stories that add the Priority Label and declared dependencies are done (2.7, 2.8), the order is the issue number alone.
/// </summary>
public static class DispatchPolicy
{
    /// <summary>The Tasks that may be dispatched, in the order given.</summary>
    public static IReadOnlyList<TaskRecord> Eligible(IEnumerable<TaskRecord> tasks) => [.. tasks.Where(task => task.State == TaskState.Queued)];

    /// <summary>The Tasks that may be dispatched into the open windows, in the order the policy puts them: by issue number, then by repository id so that the order is the same on every tick.</summary>
    public static IReadOnlyList<TaskRecord> InWindows(IEnumerable<TaskRecord> tasks, IReadOnlySet<long> openWindows) =>
        [.. Eligible(tasks).Where(task => openWindows.Contains(task.RepositoryId)).OrderBy(task => task.IssueNumber).ThenBy(task => task.RepositoryId)];

    /// <summary>The Projects whose window has nothing left to work: no Task of theirs is eligible (FR-16), so the window closes. A <c>Running</c> Task finishes all the same.</summary>
    public static IReadOnlyList<Window> Drained(IReadOnlyCollection<TaskRecord> tasks, IReadOnlyCollection<Window> windows)
    {
        var eligible = Eligible(tasks).Select(task => task.RepositoryId).ToHashSet();
        return [.. windows.Where(window => window.IsOpen && !eligible.Contains(window.RepositoryId))];
    }
}
