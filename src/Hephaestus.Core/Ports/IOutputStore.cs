using Hephaestus.Core.Output;

namespace Hephaestus.Core.Ports;

/// <summary>
/// The raw output of container runs (AD-18, FR-4, FR-53): one file for each container run, indexed in the database, so that an Agent's output
/// outlives its container and its Workspace. The runtime writes it, after the masker, and the loop and the read endpoints read it
/// (AD-3, AR-26). Persistence implements it.
/// </summary>
public interface IOutputStore
{
    /// <summary>
    /// Starts the file of a container run. A file of the same step run that an earlier attempt left is replaced, because a restart reads the
    /// whole log of a container again, so a run has one file and never two.
    /// </summary>
    Task<IOutputWriter> BeginAsync(OutputRun run, CancellationToken cancellationToken);

    /// <summary>The index entry of a step run's output, or null when none was started.</summary>
    Task<StoredOutput?> FindAsync(Guid stepRunId, CancellationToken cancellationToken);

    /// <summary>The stored output of a step run to read from the start, or null when none was started.</summary>
    Task<Stream?> OpenReadAsync(Guid stepRunId, CancellationToken cancellationToken);
}

/// <summary>
/// The file of one container run, being written. The bytes are written as they arrive and are in the file when a write returns, so output
/// written before a step was stopped survives it. Disposing a writer that was not completed leaves the file as it is, indexed as not complete.
/// </summary>
public interface IOutputWriter : IAsyncDisposable
{
    ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);

    /// <summary>Marks the file complete in the index and returns the entry.</summary>
    Task<StoredOutput> CompleteAsync(CancellationToken cancellationToken);
}
