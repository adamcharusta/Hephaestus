namespace Hephaestus.Core.Failures;

/// <summary>
/// Why a step run failed (AD-11): exactly one of these. The class is decided in one order, the runtime's cause first, then the adapter's reading of a CLI
/// that exited by itself, then Core; anything not positively identified is <see cref="Unknown"/>.
/// </summary>
public enum FailureClass
{
    UsageLimit,
    Timeout,
    AgentError,
    ValidationFailure,
    InfrastructureError,
    Cancelled,
    Unknown,
}

public static class FailureClasses
{
    /// <summary>The name stored and shown for a class; never translated.</summary>
    public static string Tag(this FailureClass failure) => failure switch
    {
        FailureClass.UsageLimit => "usage-limit",
        FailureClass.Timeout => "timeout",
        FailureClass.AgentError => "agent-error",
        FailureClass.ValidationFailure => "validation-failure",
        FailureClass.InfrastructureError => "infrastructure-error",
        FailureClass.Cancelled => "cancelled",
        FailureClass.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null),
    };

    public static bool TryParse(string? tag, out FailureClass failure)
    {
        foreach (var candidate in Enum.GetValues<FailureClass>())
        {
            if (candidate.Tag() == tag)
            {
                failure = candidate;
                return true;
            }
        }

        failure = default;
        return false;
    }
}
