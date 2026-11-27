namespace Hephaestus.Persistence.Model;

/// <summary>
/// The capacity record of an Agent credential (AD-20), keyed by the Agent's tag and the mode's tag. <see cref="ReasonId"/> is a message id and <see cref="ReasonArguments"/> its parameters
/// separated by line breaks; neither ever holds text a CLI printed. <see cref="ExhaustedUntil"/> is for the usage limit of Story 7.1.
/// </summary>
internal sealed class AgentCapacityEntry
{
    public string Agent { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;

    public string Readiness { get; set; } = string.Empty;

    public string? ReasonId { get; set; }

    public string? ReasonArguments { get; set; }

    public DateTimeOffset ProbedAt { get; set; }

    public DateTimeOffset? ExhaustedUntil { get; set; }
}
