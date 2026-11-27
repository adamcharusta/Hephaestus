using Hephaestus.Core.Ports;

namespace Hephaestus.Core.Tests.Loop;

/// <summary>An <see cref="INotifier"/> that remembers what it was asked and can be made to fail, as the real ones may.</summary>
internal sealed class RecordingNotifier : INotifier
{
    public List<(EscalationNotification Notification, string Key)> Calls { get; } = [];

    /// <summary>When set, a call fails with it, after it has been recorded.</summary>
    public Exception? Throws { get; set; }

    /// <summary>Runs just before a call is recorded, so that a test can stop the host in the middle of it.</summary>
    public Action? Before { get; set; }

    public Task NotifyAsync(EscalationNotification notification, string idempotencyKey, CancellationToken cancellationToken)
    {
        Before?.Invoke();
        Calls.Add((notification, idempotencyKey));
        return Throws is null ? Task.CompletedTask : Task.FromException(Throws);
    }
}
