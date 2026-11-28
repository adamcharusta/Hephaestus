using Hephaestus.Core.Ports;
using Hephaestus.Core.Validation;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Host.Hosting;

/// <summary>
/// The runtime as the loop holds it: the one the host registered, found when the loop first asks it for something. The loop is built when the host starts, and the runtime reads the lock of the Agent binaries and opens
/// the Docker client when it is built, so a host that never starts a Task (the Owner has opened no window) does not need either until it does (AD-7).
/// </summary>
internal sealed class LazyWorkspaceRuntime(IServiceProvider services) : IWorkspaceRuntime
{
    private IWorkspaceRuntime Runtime => services.GetRequiredService<IWorkspaceRuntime>();

    public Task<ResolvedImage> ResolveImageAsync(string image, CancellationToken cancellationToken) => Runtime.ResolveImageAsync(image, cancellationToken);

    public Task EnsureTaskVolumeAsync(Guid taskId, ResolvedImage image, string idempotencyKey, CancellationToken cancellationToken) => Runtime.EnsureTaskVolumeAsync(taskId, image, idempotencyKey, cancellationToken);

    public Task<StartedStep> StartStepAsync(StepContainerRequest request, string idempotencyKey, CancellationToken cancellationToken) => Runtime.StartStepAsync(request, idempotencyKey, cancellationToken);

    public Task CheckoutAsync(CheckoutRequest request, string idempotencyKey, CancellationToken cancellationToken) => Runtime.CheckoutAsync(request, idempotencyKey, cancellationToken);

    public Task<CheckpointResult> CheckpointAsync(CheckpointRequest request, string idempotencyKey, CancellationToken cancellationToken) => Runtime.CheckpointAsync(request, idempotencyKey, cancellationToken);

    public Task<StartedStep> StartValidationAsync(ValidationCommandRequest request, string idempotencyKey, CancellationToken cancellationToken) => Runtime.StartValidationAsync(request, idempotencyKey, cancellationToken);

    public Task<ProbeResult> RunProbeAsync(ProbeRequest request, CancellationToken cancellationToken) => Runtime.RunProbeAsync(request, cancellationToken);
}
