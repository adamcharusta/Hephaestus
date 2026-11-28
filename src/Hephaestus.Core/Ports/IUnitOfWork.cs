using Hephaestus.Core.Effects;
using Hephaestus.Core.Escalations;
using Hephaestus.Core.Runs;
using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;
using Hephaestus.Core.Windows;

namespace Hephaestus.Core.Ports;

/// <summary>A Task the loop creates for a Signal, with the snapshot of the issue that the Signal vouched for.</summary>
public sealed record NewTask(TaskRecord Task, IssueSnapshot Snapshot);

/// <summary>
/// What applying one Signal does, committed together (AD-1): the Signal leaves <see cref="SignalStatus.Pending"/>, and every Task write
/// it causes happens in the same transaction.
/// </summary>
/// <param name="Sequence">The inbox sequence of the Signal.</param>
/// <param name="Status">Applied or rejected; never pending.</param>
/// <param name="Reason">Why a Signal was rejected, one of <see cref="SignalRejections"/>; null for an applied one.</param>
/// <param name="TaskId">The Task the Signal concerned, when it concerned one that exists; a new Task's id is taken from <paramref name="NewTask"/>.</param>
/// <param name="NewTask">The Task this Signal creates, if it does.</param>
/// <param name="NewEffects">The effects this Signal causes, whose intents commit in the same transaction (AD-5); each is for the Task of <paramref name="NewTask"/> or <paramref name="TaskId"/>.</param>
/// <param name="MoveTaskTo">The state the Task of <paramref name="TaskId"/> moves to in the same transaction, when the Signal takes it out of the state it is in.</param>
/// <param name="EndEscalation">How the Task's open Escalation ends, when the move takes the Task out of <c>waiting-for-owner</c>: answered, or cancelled or closed without an answer.</param>
/// <param name="AdmitComment">The comment of a Trusted User that is admitted to the Task's context in the same transaction; its <see cref="AdmittedComment.TaskId"/> is the Task of <paramref name="TaskId"/>.</param>
/// <param name="OpenWindow">The window this Signal opens for its Project (FR-16); a Project has at most one open, which the database enforces.</param>
/// <param name="CloseWindow">The window this Signal closes.</param>
/// <param name="StepRun">The change of a step run this Signal causes: the end of a step, as the runtime reported it (AD-18).</param>
public sealed record SignalApplication(
    long Sequence,
    SignalStatus Status,
    string? Reason = null,
    Guid? TaskId = null,
    NewTask? NewTask = null,
    IReadOnlyList<NewEffect>? NewEffects = null,
    TaskState? MoveTaskTo = null,
    EscalationOutcome? EndEscalation = null,
    AdmittedComment? AdmitComment = null,
    Window? OpenWindow = null,
    WindowClosing? CloseWindow = null,
    StepRunChange? StepRun = null);

/// <summary>The end of a window, with the reason: the Owner stopped it, or its Project had nothing left to work.</summary>
public sealed record WindowClosing(Guid WindowId, WindowEnd End);

/// <summary>
/// The write side of Task state (AD-1). Task and step-run records are written only by the orchestrator loop through this port, and
/// nothing else may depend on it: an architecture test fails when any other code does. Persistence implements it.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Applies one Signal in one transaction: marks it, writes the Task that <see cref="SignalApplication.NewTask"/> names, and journals
    /// the intents of the effects it causes (AD-5). A Signal that is no longer pending is left as it is and nothing is written. Returns
    /// whether the Signal was applied now.
    /// </summary>
    Task<bool> ApplyAsync(SignalApplication application, CancellationToken cancellationToken);

    /// <summary>
    /// Appends to the journal what the loop found after it asked the far side (AD-5), in one transaction: a retry that records when the effect
    /// is tried next, or the result that ends it, with the move of the Task and the intents the move causes when the effect could not be
    /// completed. An effect that has a result already is left as it is and nothing is written. Returns whether this was recorded now.
    /// </summary>
    Task<bool> RecordEffectAsync(EffectRecording recording, CancellationToken cancellationToken);

    /// <summary>
    /// Raises an Escalation (FR-48) in one transaction: records it, moves the Task to <c>waiting-for-owner</c>, and journals the intents of the
    /// state label, the comment, and the notification (AD-5). Nothing is written, and false is returned, when the Task is no longer in the state
    /// the plan was made for, so that one Escalation asks for one decision.
    /// </summary>
    Task<bool> EscalateAsync(EscalationPlan plan, CancellationToken cancellationToken);

    /// <summary>
    /// Starts a Task (AD-1): in one transaction, moves it from <c>Queued</c> to <c>Running</c>, stores its snapshot, plans its first step run, and journals the intent of the label that shows the state. The move is made
    /// only while the Task is <c>Queued</c> and no Task is <c>Running</c>: the database refuses a second, so that two Tasks never run. Returns false, and writes nothing, when it was not.
    /// </summary>
    Task<bool> DispatchAsync(TaskDispatch dispatch, CancellationToken cancellationToken);

    /// <summary>Plans a further step run of a <c>Running</c> Task, with its deadline, before its container is asked for (AD-18). Returns false, and writes nothing, when the Task is not <c>Running</c> or the run is recorded already.</summary>
    Task<bool> PlanStepRunAsync(NewStepRun run, CancellationToken cancellationToken);

    /// <summary>
    /// Changes a step run in one transaction with the intents and the move of the Task it causes (AD-1, AD-5). Returns false, and writes nothing, when the run is not in a status the change may be made from, so that
    /// recording a change twice makes it once.
    /// </summary>
    Task<bool> ChangeStepRunAsync(StepRunChange change, CancellationToken cancellationToken);

    /// <summary>Closes a window the loop found drained (FR-16). Returns false when it is closed already.</summary>
    Task<bool> CloseWindowAsync(WindowClosing closing, CancellationToken cancellationToken);
}
