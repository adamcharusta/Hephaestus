using Hephaestus.Core.Ports;

namespace Hephaestus.Host.Hosting;

/// <summary>
/// The <see cref="INotifier"/> of v0.1, which does nothing (AR-63): the Owner is told that a Task needs a decision by the comment on its issue,
/// and a channel that reaches the Owner elsewhere, email or a phone push, is v0.2's (FR-50). The loop still calls it, and journals the call, so
/// that a notifier that does something changes nothing about how a Task ends.
/// </summary>
internal sealed class NoOpNotifier : INotifier
{
    public Task NotifyAsync(EscalationNotification notification, string idempotencyKey, CancellationToken cancellationToken) => Task.CompletedTask;
}
