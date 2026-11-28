using Hephaestus.Core.Validation;
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

    /// <summary>
    /// Keeps the work of a step that ended, for any reason, as one commit and one bundle (AD-9, FR-20), in a credential-free container of the <c>hephaestus-tools</c> image that never uses the volume's <c>.git</c>:
    /// it builds a git directory of its own from the last pushed tip, takes <c>/workspace</c> as work tree only, and writes one commit whose parent is that tip, flattening what the Agent committed. The runtime reads exactly one
    /// regular file of bounded size from the stopped container and returns it as a bundle reference with its path, checksum, tip, and prerequisite; a tree that is the tip's makes no commit and no bundle. The base bundle's
    /// file is deleted whatever came of it, and the result's file is the loop's to delete once its push is journaled.
    /// </summary>
    /// <exception cref="WorkspaceRuntimeException">The base bundle is not the file the reference describes, the tools image cannot be fetched, the container failed or did not end in time, or what it returned is not exactly one bundle of the size and the shape that was asked for (an infrastructure error: nothing of it is used).</exception>
    Task<CheckpointResult> CheckpointAsync(CheckpointRequest request, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Starts one Validation command on a fresh checkout of the pushed tip (AD-6, AD-13, FR-31): <see cref="CheckoutAsync"/> first, so that nothing an earlier command or step left in the Task's volume is there, then a container of the Task's
    /// image that mounts the Task volume and nothing else of a step, with no credential, no context, no Methodology, and no Agent CLI, and with the Project's egress list alone. It returns as soon as the container has started, as
    /// <see cref="StartStepAsync"/> does: the runtime stops it at its deadline, keeps its output, and appends the one step-exited Signal (AD-18). When the command's container exists already, which is what a restart finds, the tree is not made again under a command that runs.
    /// Nothing the command leaves in the tree is kept: Validation is never checkpointed, and the next checkout makes the tree anew.
    /// </summary>
    /// <exception cref="ArgumentException">The container cannot be made, as for <see cref="StartStepAsync"/>.</exception>
    /// <exception cref="WorkspaceRuntimeException">The checkout failed, or the container's isolation cannot be given, as for <see cref="CheckoutAsync"/> and <see cref="StartStepAsync"/>.</exception>
    Task<StartedStep> StartValidationAsync(ValidationCommandRequest request, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Runs an Agent probe to its end (AD-20, AD-18): a container of the probe image with no Task volume, no context, and no Methodology, whose only way out is its own egress proxy, limited to the hosts the invocation
    /// declares. The invocation's secret references are resolved through <c>ISecretStore</c> when the container is created and nowhere else. It returns when the container has ended or has been stopped at its limit, and has been
    /// removed with its proxy and network. What it printed is masked and bounded. A probe is not a step run: it appends no Signal and is journaled nowhere.
    /// </summary>
    /// <exception cref="ImageNotUsableException">The probe image cannot be fetched or is not built for this machine.</exception>
    /// <exception cref="WorkspaceRuntimeException">A secret the invocation references is not stored, the proxy did not start, or isolation cannot be given (AD-7).</exception>
    Task<ProbeResult> RunProbeAsync(ProbeRequest request, CancellationToken cancellationToken);
}
