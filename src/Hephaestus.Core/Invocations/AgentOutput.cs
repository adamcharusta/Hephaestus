namespace Hephaestus.Core.Invocations;

/// <summary>What an event of an Agent's output stream is, the same for every Agent (AD-10).</summary>
public enum AgentEventKind
{
    /// <summary>The CLI began a session; <see cref="AgentEvent.Text"/> names the Model it resolved.</summary>
    Started,

    /// <summary>The Agent said something.</summary>
    Message,

    /// <summary>The Agent called a tool; <see cref="AgentEvent.Name"/> is the tool and <see cref="AgentEvent.Text"/> its input.</summary>
    ToolCall,

    /// <summary>A tool answered; <see cref="AgentEvent.Name"/> is the tool when the stream said which call it answers.</summary>
    ToolResult,

    /// <summary>The CLI reported where the account stands against a limit; <see cref="AgentEvent.Name"/> is the kind of limit and <see cref="AgentEvent.Text"/> its status.</summary>
    RateLimit,
}

/// <summary>One event of an output stream, normalized. <see cref="Text"/> is cut at <see cref="MaximumTextLength"/> characters.</summary>
public sealed record AgentEvent(AgentEventKind Kind, string? Name, string? Text, bool IsError, DateTimeOffset? At)
{
    public const int MaximumTextLength = 2_000;
}

/// <summary>What a run used, as the CLI reported it. A field the CLI did not report is null, never zero (the Consistency Conventions).</summary>
public sealed record AgentUsage(long? InputTokens, long? OutputTokens, long? CacheReadTokens, long? CacheCreationTokens, decimal? CostUsd);

/// <summary>The end of a run as the CLI reported it.</summary>
/// <param name="IsError">The CLI says the run ended in an error.</param>
/// <param name="Text">What the Agent answered last, as text.</param>
/// <param name="StructuredOutput">The document the Agent produced under the result schema, as JSON text; null when no schema was given or none was produced.</param>
/// <param name="Model">The Model the CLI resolved, when it said.</param>
/// <param name="Usage">Null when the CLI reported no usage.</param>
/// <param name="Error">What the CLI named as the error, as it spelled it; null when it named none.</param>
public sealed record AgentResult(bool IsError, string? Text, string? StructuredOutput, string? Model, AgentUsage? Usage, string? Error);

/// <summary>How the account stands against a limit, as the CLI reported it. What a status means is Story 7.1's to settle; nothing here treats one as a usage limit.</summary>
public sealed record AgentRateLimit(string Type, string Status, DateTimeOffset? ResetsAt);

/// <summary>A stored output stream, parsed (AD-10).</summary>
/// <param name="Events">In the order the stream printed them.</param>
/// <param name="Result">The last result the CLI printed; null when the stream has none, as when the CLI was stopped.</param>
/// <param name="RateLimits">The limit reports of the stream, in order.</param>
/// <param name="UnreadableLines">How many non-blank lines were not events of the CLI's stream; they are skipped, never an error.</param>
public sealed record AgentOutput(IReadOnlyList<AgentEvent> Events, AgentResult? Result, IReadOnlyList<AgentRateLimit> RateLimits, int UnreadableLines);
