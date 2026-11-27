using Hephaestus.Core.Signals;
using Hephaestus.Core.Tasks;

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
public sealed record SignalApplication(long Sequence, SignalStatus Status, string? Reason = null, Guid? TaskId = null, NewTask? NewTask = null);

/// <summary>
/// The write side of Task state (AD-1). Task and step-run records are written only by the orchestrator loop through this port, and
/// nothing else may depend on it: an architecture test fails when any other code does. Persistence implements it.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Applies one Signal in one transaction: marks it, and writes the Task that <see cref="SignalApplication.NewTask"/> names. A
    /// Signal that is no longer pending is left as it is and nothing is written. Returns whether the Signal was applied now.
    /// </summary>
    Task<bool> ApplyAsync(SignalApplication application, CancellationToken cancellationToken);
}
