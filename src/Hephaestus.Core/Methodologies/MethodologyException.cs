using Hephaestus.Core.Localization;

namespace Hephaestus.Core.Methodologies;

/// <summary>The instructions of a step cannot be chosen: the Methodology a Project names is not shipped, or it has no template for the step kind. The reason is a message for the Owner.</summary>
public sealed class MethodologyException(Message reason) : InvalidOperationException(reason.Id)
{
    public Message Reason { get; } = reason;
}
