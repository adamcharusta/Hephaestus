using Hephaestus.Core.Failures;

namespace Hephaestus.Core.Validation;

/// <summary>
/// How Validation ended (AD-13, AD-11): it passed, or it failed for exactly one of the classes of a failed step run. Only what the runtime saw decides it: the exit status of each command and why its container ended.
/// </summary>
public enum ValidationOutcome
{
    /// <summary>Every command ran and exited with status 0.</summary>
    Passed,

    /// <summary>A command exited with a status other than 0 (FR-31).</summary>
    ValidationFailure,

    /// <summary>A command ran past its deadline and the runtime stopped it (FR-19).</summary>
    Timeout,

    /// <summary>The runtime could not see a command to its end: its container was killed for using more memory than its limit, or was gone when the runtime looked (AD-11).</summary>
    InfrastructureError,
}

public static class ValidationOutcomes
{
    /// <summary>The name stored and shown for an outcome; never translated. A failed one is the tag of its <see cref="FailureClass"/>.</summary>
    public static string Tag(this ValidationOutcome outcome) => outcome switch
    {
        ValidationOutcome.Passed => "passed",
        ValidationOutcome.ValidationFailure => FailureClass.ValidationFailure.Tag(),
        ValidationOutcome.Timeout => FailureClass.Timeout.Tag(),
        ValidationOutcome.InfrastructureError => FailureClass.InfrastructureError.Tag(),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    /// <summary>The class of the failed step run that records a failed Validation, or null when it passed.</summary>
    public static FailureClass? Failure(this ValidationOutcome outcome) => outcome switch
    {
        ValidationOutcome.Passed => null,
        ValidationOutcome.ValidationFailure => FailureClass.ValidationFailure,
        ValidationOutcome.Timeout => FailureClass.Timeout,
        ValidationOutcome.InfrastructureError => FailureClass.InfrastructureError,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };
}
