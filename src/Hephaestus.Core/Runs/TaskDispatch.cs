using Hephaestus.Core.Effects;

namespace Hephaestus.Core.Runs;

/// <summary>
/// What starting a Task writes, in one transaction (AD-1): the Task moves from <c>Queued</c> to <c>Running</c>, which is the only edge into that state; the snapshot it starts with is stored, unless it has one
/// from an earlier start; its first step run is planned with the deadline it will be stopped at; and the intent of the label that shows the new state is journaled (AD-5). Nothing is written, and false is
/// returned, when the Task is no longer <c>Queued</c> or another Task is <c>Running</c>.
/// </summary>
/// <param name="Snapshot">The snapshot to store; null for a Task that has one already.</param>
public sealed record TaskDispatch(Guid TaskId, TaskSnapshot? Snapshot, NewStepRun FirstStep, IReadOnlyList<NewEffect> Effects);
