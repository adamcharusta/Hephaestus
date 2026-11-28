namespace Hephaestus.Persistence.Model;

/// <summary>One step of a Task (AD-1, AD-18). Only the loop writes it, through <c>IUnitOfWork</c>.</summary>
internal sealed class StepRunEntry
{
    public Guid Id { get; set; }

    public Guid TaskId { get; set; }

    /// <summary>The step's place in the Task's history, from 1; unique for a Task.</summary>
    public int Sequence { get; set; }

    public string Kind { get; set; } = string.Empty;

    public int Round { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset Deadline { get; set; }

    public DateTimeOffset PlannedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public string? Agent { get; set; }

    public string? Model { get; set; }

    public string? Effort { get; set; }

    public string? Methodology { get; set; }

    public string? Template { get; set; }

    public int? CommandIndex { get; set; }

    public int? ExitCode { get; set; }

    public string? Cause { get; set; }

    public string? Failure { get; set; }

    public string? Outcome { get; set; }

    public string? Result { get; set; }

    public string TipBefore { get; set; } = string.Empty;

    public string? TipAfter { get; set; }
}

/// <summary>What a Task started with (FR-2, AD-16): the commit the Project Configuration was read at, the image it resolved to, and the document that holds all of it. One for a Task.</summary>
internal sealed class TaskSnapshotEntry
{
    public Guid TaskId { get; set; }

    public string CommitId { get; set; } = string.Empty;

    public string ImageDigest { get; set; } = string.Empty;

    public string Document { get; set; } = string.Empty;

    public DateTimeOffset TakenAt { get; set; }
}
