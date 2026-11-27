using Hephaestus.Core.Signals;

namespace Hephaestus.Core.Ports;

/// <summary>
/// The persisted inbox of Signals (AD-1). Changes reach Task state only as Signals appended here; Persistence implements it, and the
/// poller, the control API, and the runtime append. Inbox order is a database-assigned integer sequence.
/// </summary>
public interface ISignalInbox
{
    /// <summary>
    /// Appends the Signals in one transaction, in the order given. A Signal whose source and source event id are already in the inbox is
    /// not appended again and is not an error. Returns how many were new.
    /// </summary>
    Task<int> AppendAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken);

    /// <summary>The Signals whose sequence is greater than <paramref name="afterSequence"/>, in inbox order, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<StoredSignal>> ReadAsync(long afterSequence, int limit, CancellationToken cancellationToken);

    /// <summary>The Signals nobody has applied or rejected yet, in inbox order, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<StoredSignal>> ReadPendingAsync(int limit, CancellationToken cancellationToken);
}
