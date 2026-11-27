using Hephaestus.Core.Escalations;

namespace Hephaestus.Core.Ports;

/// <summary>What a Notification says: that a Task needs the Owner, and where. It carries no text of the question, which stays on GitHub (AR-63).</summary>
public sealed record EscalationNotification(Guid TaskId, long RepositoryId, int IssueNumber, Guid EscalationId, EscalationKind Kind);

/// <summary>
/// The port that tells the Owner, off GitHub, that an Escalation was raised (FR-50). Before v0.2 the implementation in <c>Hephaestus.Host</c>
/// does nothing, and the Owner is told by the comment on the issue (AR-63). The call is an effect (AD-5): the loop journals it, makes it once,
/// and never makes it again after a restart. A Task's outcome is the same whether it is delivered, fails, or does nothing (FR-51).
/// </summary>
public interface INotifier
{
    /// <summary>Notifies the Owner. <paramref name="idempotencyKey"/> is the key the loop journaled for the effect; the notifier neither journals nor derives keys.</summary>
    Task NotifyAsync(EscalationNotification notification, string idempotencyKey, CancellationToken cancellationToken);
}
