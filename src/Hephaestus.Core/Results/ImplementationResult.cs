namespace Hephaestus.Core.Results;

/// <summary>A question an Agent cannot settle by itself and the answers it offers; it becomes an Escalation (AD-13).</summary>
public sealed record DecisionQuestion(string Question, IReadOnlyList<string> Options);

/// <summary>
/// The result of an <c>Implementation</c>, <c>Correction</c>, or <c>Rework</c> step (AD-13): an implementation summary and an outcome. A <see cref="StepOutcome.NeedsDecision"/>
/// carries the questions; a <see cref="StepOutcome.Completed"/> carries none. Every property is required, so that the schema fits what both CLIs' constrained output accepts.
/// </summary>
public sealed record ImplementationResult(
    StepOutcome Outcome,
    string Summary,
    IReadOnlyList<DecisionQuestion> Questions);
