using Hephaestus.Core.Aliases;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Localization;

namespace Hephaestus.Core.Readiness;

/// <summary>What a probe found of an Agent credential.</summary>
public enum AgentReadiness
{
    /// <summary>The CLI answered with the credential.</summary>
    Ready,

    /// <summary>The CLI could not be used with the credential; <see cref="AgentCapacity.Reason"/> says why.</summary>
    Unavailable,
}

public static class AgentReadinesses
{
    /// <summary>The name stored and shown; never translated.</summary>
    public static string Tag(this AgentReadiness readiness) => readiness switch
    {
        AgentReadiness.Ready => "ready",
        AgentReadiness.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(readiness), readiness, null),
    };

    public static bool TryParse(string? tag, out AgentReadiness readiness)
    {
        foreach (var candidate in Enum.GetValues<AgentReadiness>())
        {
            if (candidate.Tag() == tag)
            {
                readiness = candidate;
                return true;
            }
        }

        readiness = default;
        return false;
    }
}

/// <summary>
/// The capacity record of an Agent credential (AD-20): what the last probe found, and when. Capacity is a fact about the Agent and not a failure of a Task, so an
/// Agent found unavailable has no capacity and no step run is failed by it. The reason is a message id with its arguments, never text the CLI printed, so that
/// what is stored is the same in every language and holds nothing an Agent or a vendor wrote.
/// </summary>
/// <param name="Mode">Which credential of the Agent this is: the secret of one mode.</param>
/// <param name="ReasonId">The message id of why the Agent is unavailable; null when it is ready.</param>
/// <param name="ReasonArguments">The parameters of the reason, as text.</param>
/// <param name="ProbedAt">When the probe ended.</param>
/// <param name="ExhaustedUntil">Until when the Agent has no capacity because of a usage limit; null when none is known. Story 7.1 writes it.</param>
public sealed record AgentCapacity(
    Agent Agent,
    AuthenticationMode Mode,
    AgentReadiness Readiness,
    string? ReasonId,
    IReadOnlyList<string> ReasonArguments,
    DateTimeOffset ProbedAt,
    DateTimeOffset? ExhaustedUntil)
{
    /// <summary>Whether Dispatch may choose the Agent now: the last probe found it ready, and no usage limit holds it.</summary>
    public bool HasCapacity(DateTimeOffset now) => Readiness == AgentReadiness.Ready && (ExhaustedUntil is not { } until || until <= now);

    /// <summary>The reason as a sentence not yet written; null when the Agent is ready.</summary>
    public Message? Reason => ReasonId is null ? null : new Message(ReasonId, [.. ReasonArguments]);
}
