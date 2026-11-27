namespace Hephaestus.Core.Tasks;

/// <summary>
/// Which Tasks may be dispatched, computed from recorded state and never from a queue held in memory (AD-1). Only a <c>Queued</c> Task is
/// eligible, so a Task in <c>WaitingForOwner</c>, which has asked the Owner a question, takes no part and holds no other Task back: the
/// question of one Task never stalls the rest (FR-48). Stories 2.7 and 2.8 add the order and the dependencies to this policy.
/// </summary>
public static class DispatchPolicy
{
    /// <summary>The Tasks that may be dispatched, in the order given.</summary>
    public static IReadOnlyList<TaskRecord> Eligible(IEnumerable<TaskRecord> tasks) => [.. tasks.Where(task => task.State == TaskState.Queued)];
}
