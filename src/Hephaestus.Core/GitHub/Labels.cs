using Hephaestus.Core.Tasks;

namespace Hephaestus.Core.GitHub;

/// <summary>The label conventions on GitHub (AR-33); part of the versioned public surface and never translated.</summary>
public static class Labels
{
    /// <summary>The Marker Label: applied by a Trusted User, it opts an issue in.</summary>
    public const string Marker = "hephaestus";

    /// <summary>What a state label starts with: the label of a Task in a state is <c>hephaestus:&lt;state&gt;</c>, the state in kebab-case.</summary>
    public const string StatePrefix = "hephaestus:";

    /// <summary>GitHub treats label names as case-insensitive, so a label is the Marker Label whatever its case.</summary>
    public static bool IsMarker(string? label) => string.Equals(label, Marker, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The name of a label Hephaestus reads as intent from a Trusted User, as it is written here whatever its case, or null for a label it does
    /// not read. The same label applied by anyone else is reported on the issue (AD-15). The Role Labels and the Priority Label join the Marker
    /// Label here with the stories that read them; a state label is not one, because it is a projection and not intent.
    /// </summary>
    public static string? ReadLabel(string? label) => IsMarker(label) ? Marker : null;

    /// <summary>The label that shows a Task in <paramref name="state"/> on its issue (FR-11).</summary>
    public static string State(TaskState state) => StatePrefix + state.Tag();

    /// <summary>Whether the label is the state label of some state, whatever its case; a label under the prefix that names no state is not one.</summary>
    public static bool TryParseState(string? label, out TaskState state)
    {
        if (label is not null && label.StartsWith(StatePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return TaskStates.TryParse(label[StatePrefix.Length..].ToLowerInvariant(), out state);
        }

        state = default;
        return false;
    }

    /// <summary>
    /// What changes a label set so that it holds the state label of <paramref name="state"/> and no other state label (a set operation, so
    /// that doing it again changes nothing, AD-5). Labels that are not state labels are never touched.
    /// </summary>
    public static (IReadOnlyList<string> Add, IReadOnlyList<string> Remove) PlanState(IEnumerable<string> present, TaskState state)
    {
        var wanted = State(state);
        var labels = present.ToList();
        var add = labels.Any(label => string.Equals(label, wanted, StringComparison.OrdinalIgnoreCase)) ? [] : new List<string> { wanted };
        var remove = labels.Where(label => TryParseState(label, out var other) && other != state).ToList();
        return (add, remove);
    }
}
