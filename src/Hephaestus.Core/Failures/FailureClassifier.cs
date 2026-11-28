using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Failures;

/// <summary>
/// The one order in which the class of a failed Agent step is decided (AD-11, NFR-11): the runtime's own cause first, then the adapter's reading of a CLI that ended by itself, then Core's reading of the result. A step that no one
/// positively identified is <see cref="FailureClass.Unknown"/>, which the adapter answers for a run it cannot read; it is never a usage limit, which only an adapter that finds the limit in the stream can name (FR-27, AR-64).
/// </summary>
public static class FailureClassifier
{
    /// <summary>The class the runtime's cause names: a deadline is a <c>timeout</c>, and a container lost or killed for its memory is an <c>infrastructure-error</c>; null when the container ended by itself.</summary>
    public static FailureClass? FromRuntime(StepExitCause cause) => cause switch
    {
        StepExitCause.Timeout => FailureClass.Timeout,
        StepExitCause.OutOfMemory or StepExitCause.Lost => FailureClass.InfrastructureError,
        _ => null,
    };

    /// <summary>
    /// The class of an Agent step that ended, or null when it did not fail. <paramref name="adapterReading"/> and <paramref name="resultIsWhole"/> are asked only when the question reaches them, in that order, so that a stream is read
    /// for a container that ended by itself and not for one the runtime stopped.
    /// </summary>
    /// <param name="adapterReading">The adapter's answer for a CLI that ended by itself: a class, or null when it ended well.</param>
    /// <param name="resultIsWhole">Whether the result document was found and is valid by strict deserialization; a missing or invalid result is an <c>agent-error</c> and is never interpreted (AD-13).</param>
    public static FailureClass? Decide(StepExitCause cause, Func<FailureClass?> adapterReading, Func<bool> resultIsWhole)
    {
        ArgumentNullException.ThrowIfNull(adapterReading);
        ArgumentNullException.ThrowIfNull(resultIsWhole);
        return FromRuntime(cause) ?? adapterReading() ?? (resultIsWhole() ? null : FailureClass.AgentError);
    }
}
