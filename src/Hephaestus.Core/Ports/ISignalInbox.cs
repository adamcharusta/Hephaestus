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

    /// <summary>
    /// Appends one Signal and returns its sequence, which is the Signal's id for whoever asked for it (AD-17): a request of the Owner is answered with it, and
    /// its status is read by it. A Signal whose source and source event id are in the inbox already is not appended again, and the sequence of the one that is there is returned.
    /// </summary>
    Task<long> AppendOneAsync(Signal signal, CancellationToken cancellationToken);

    /// <summary>The Signal with this sequence, or null when the inbox has none.</summary>
    Task<StoredSignal?> FindAsync(long sequence, CancellationToken cancellationToken);

    /// <summary>The Signals whose sequence is greater than <paramref name="afterSequence"/>, in inbox order, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<StoredSignal>> ReadAsync(long afterSequence, int limit, CancellationToken cancellationToken);

    /// <summary>The Signals nobody has applied or rejected yet, in inbox order, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<StoredSignal>> ReadPendingAsync(int limit, CancellationToken cancellationToken);
}
