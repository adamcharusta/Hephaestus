using Hephaestus.Core.Workspaces;

namespace Hephaestus.Core.Ports;

/// <summary>
/// The Docker runtime behind the loop (AD-6, AD-7, AD-18). Everything it creates carries the labels of the installation it serves, so that recovery and
/// cleanup can select on them. Every method that creates something takes the idempotency key of the effect that causes it (AD-5), which is recorded on what is
/// created. A retry after a crash makes no second object: a step run's container is found by its labels, and the Task's volume, the step run's network and proxy, and the helpers by their fixed names.
/// </summary>
public interface IWorkspaceRuntime
{
    /// <summary>
    /// Resolves a Project's image when a Task starts (AD-8): fetches it if the machine lacks it, and establishes its digest, its processor
    /// architecture, and its C library without running anything of it.
    /// </summary>
    /// <exception cref="ImageNotUsableException">The image cannot be fetched, is not built for this machine, declares volumes, or its C library cannot be established.</exception>
    Task<ResolvedImage> ResolveImageAsync(string image, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the Task's one named volume, <c>hephaestus-ws-&lt;task-id&gt;</c>, and makes its root writable by the step user (AD-6). A volume that
    /// exists is left as it is. <paramref name="image"/> only gives the helper that sets the owner something to be created from; nothing in it runs.
    /// </summary>
    Task EnsureTaskVolumeAsync(Guid taskId, ResolvedImage image, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the hardened container of a step run and starts it, and returns as soon as it has been started: it does not wait for the step to
    /// end (AD-18). When a container of the step run exists already, which is what a restart after a crash between the intent and its result finds,
    /// that container is returned and a second one is not made.
    /// </summary>
    /// <exception cref="ArgumentException">The request cannot be carried out: no command, a limit that Docker would read as no limit, an environment name that cannot be one, or files that collide.</exception>
    /// <exception cref="WorkspaceRuntimeException">A network of the step run exists and is not an internal one, the outbound network exists and is an internal one, or the step run's egress proxy did not start (AD-7).</exception>
    Task<StartedStep> StartStepAsync(StepContainerRequest request, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Makes <c>/workspace</c> of the Task's volume a fresh clone of the commit in the request's bundle, in a credential-free container of the <c>hephaestus-tools</c> image (AD-6, FR-17):
    /// nothing a step left in the volume survives, not a hook, a configuration entry, or an ignored file. It returns when the container has ended and has been removed; the bundle's file is
    /// deleted whatever came of it, because it is read once. The Task's volume exists already (<see cref="EnsureTaskVolumeAsync"/>), and a retry after a crash makes the same workspace.
    /// </summary>
    /// <exception cref="WorkspaceRuntimeException">The bundle is not the file the reference describes, the tools image cannot be fetched, or the checkout ended with a failure or did not end in time.</exception>
    Task CheckoutAsync(CheckoutRequest request, string idempotencyKey, CancellationToken cancellationToken);
}
