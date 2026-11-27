namespace Hephaestus.Core.Output;

/// <summary>
/// The container run whose output is being stored (AD-18): the step run it belongs to, which is the key of its one file, the Task, and the
/// container that produced it.
/// </summary>
public sealed record OutputRun(Guid TaskId, Guid StepRunId, string ContainerId);

/// <summary>
/// What the database knows of the output file of a container run (FR-53): who produced it, how much of it is stored, and whether the run
/// ended and the file was completed. A file that is not complete holds what was written before the writer was disposed, for example because
/// the host stopped; it is preserved, never discarded.
/// </summary>
/// <param name="Bytes">The size of the stored, masked output as the index last recorded it, which is when the file was completed or its writer disposed; a run that was killed leaves what it was started with, and the file is longer.</param>
/// <param name="CompletedAt">When the file was completed; null while it is not.</param>
public sealed record StoredOutput(Guid TaskId, Guid StepRunId, string ContainerId, long Bytes, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt)
{
    public bool Complete => CompletedAt is not null;
}
