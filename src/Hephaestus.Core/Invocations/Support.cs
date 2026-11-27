using Hephaestus.Core.Localization;

namespace Hephaestus.Core.Invocations;

/// <summary>
/// Whether an adapter supports something, such as an authentication mode or an Effort (AD-7, AD-10): a capability a CLI lacks is declared
/// absent with the reason, never imitated.
/// </summary>
public readonly record struct Support
{
    private Support(bool isSupported, Message? reason)
    {
        IsSupported = isSupported;
        Reason = reason;
    }

    public bool IsSupported { get; }

    /// <summary>Why not, as a message for the Owner; null when supported.</summary>
    public Message? Reason { get; }

    public static Support Yes { get; } = new(true, null);

    public static Support No(Message reason) => new(false, reason);
}

/// <summary>An invocation was asked for with a mode or an Effort its adapter declares unsupported; whoever asked should have checked first.</summary>
public sealed class UnsupportedInvocationException(Message reason) : InvalidOperationException(reason.Id)
{
    public Message Reason { get; } = reason;
}
