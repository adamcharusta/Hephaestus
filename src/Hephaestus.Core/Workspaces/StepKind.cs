namespace Hephaestus.Core.Workspaces;

/// <summary>
/// What a step is for (AD-13, AD-6): the kinds of step an Agent performs. Only the kinds that change code are checkpointed; what any other leaves in the tree is
/// discarded by the next checkout.
/// </summary>
public enum StepKind
{
    Readiness,
    Planning,
    Implementation,
    Correction,
    Rework,
    Review,
}

public static class StepKinds
{
    /// <summary>The name a commit trailer, a document, or a configuration file carries; never translated.</summary>
    public static string Tag(this StepKind kind) => kind switch
    {
        StepKind.Readiness => "readiness",
        StepKind.Planning => "planning",
        StepKind.Implementation => "implementation",
        StepKind.Correction => "correction",
        StepKind.Rework => "rework",
        StepKind.Review => "review",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Whether the work of a step of this kind is kept as a commit (AD-6, AD-9): <c>Implementation</c>, <c>Correction</c>, and <c>Rework</c>.</summary>
    public static bool IsCheckpointed(this StepKind kind) => kind is StepKind.Implementation or StepKind.Correction or StepKind.Rework;
}
