using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using Message = Hephaestus.Core.Localization.Message;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Secrets;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces;

/// <summary>
/// The Docker runtime (AD-6, AD-7, AD-18, Stories 3.2 and 3.3). It creates the hardened container of a step and starts it without waiting for the step to
/// end, hands it to the <see cref="IStepWatcher"/>, which sees it to its end, creates and prepares the Task's volume, and resolves a Project's image. It is
/// the only code that talks to the Docker socket, and it writes no journal: the loop does, and passes the idempotency key of each effect. Every step run has an internal
/// network of its own and an egress proxy on it, which is the only way out of the network (Story 3.4). It resolves the secret references of an Agent invocation through <paramref name="secrets"/>, when it creates the
/// container and nowhere else, and masks what an Agent probe printed with <paramref name="masker"/> (Story 3.10).
/// </summary>
public sealed partial class DockerWorkspaceRuntime(IDockerClient docker, WorkspaceRuntimeOptions options, IStepWatcher watcher, ISecretStore secrets, SecretMasker masker) : IWorkspaceRuntime
{
    private const string FillContextDirectory = "/context";
    private const string FillMethodologyDirectory = "/methodology";

    public async Task EnsureTaskVolumeAsync(Guid taskId, ResolvedImage image, string idempotencyKey, CancellationToken cancellationToken)
    {
        var volume = WorkspaceLabels.TaskVolume(taskId);
        await CreateVolumeAsync(volume, WorkspaceLabels.For(options.InstallationId, taskId, null, idempotencyKey), cancellationToken);

        // Docker makes the root of a new volume belong to root, and the step user could not create a file in it. The owner is set at every
        // call, because a crash between the volume and the owner would leave a volume that exists and cannot be written; setting it again is harmless.
        // The helper mounts the volume with volume-nocopy like every container that does, so nothing the image holds at /workspace enters it (AD-6).
        var helper = WorkspaceLabels.Helper(WorkspaceLabels.VolumeHelperRole, taskId);
        await RemoveIfExistsAsync(helper, cancellationToken);
        var created = await docker.Containers.CreateContainerAsync(
            InertContainer(helper, image.Reference, WorkspaceLabels.For(options.InstallationId, taskId, null, idempotencyKey, WorkspaceLabels.VolumeHelperRole), [VolumeMount(volume, StepContainerLayout.Workspace, readOnly: false)]),
            cancellationToken);
        try
        {
            using var owner = TarFiles.OwnedDirectory(StepContainerLayout.Workspace.TrimStart('/'), StepContainerLayout.UserId, StepContainerLayout.GroupId);
            await docker.Containers.ExtractArchiveToContainerAsync(created.ID, new CopyToContainerParameters { Path = "/", CopyUIDGID = true }, owner, cancellationToken);
        }
        finally
        {
            await RemoveQuietlyAsync(created.ID);
        }
    }

    public async Task<StartedStep> StartStepAsync(StepContainerRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Whatever is wrong with the request is found before anything is created, so that a refusal leaves no network, volume, or helper behind.
        var definition = StepContainer(request, idempotencyKey);
        ValidateFiles(request.Context);
        ValidateFiles(request.Methodology);

        // A crash between the intent to create the container and its result leaves it, or nothing. What is there is found by its labels
        // and is the container of this step run; it is never made twice (AD-5).
        if (await FindStepContainerAsync(request.StepRunId, cancellationToken) is { } existing)
        {
            // A container that has not started yet is given its proxy first, if the crash took that too; one that runs keeps what it has.
            if (string.Equals(existing.State, "created", StringComparison.Ordinal) && !await EgressProxyRunsAsync(request.StepRunId, cancellationToken))
            {
                await EnsureNetworkAsync(options.OutboundNetwork, isInternal: false, WorkspaceLabels.For(options.InstallationId, null, null, idempotencyKey), cancellationToken);
                await EnsureNetworkAsync(WorkspaceLabels.StepNetwork(request.StepRunId), isInternal: true, WorkspaceLabels.For(options.InstallationId, TaskLabel(request), request.StepRunId, idempotencyKey), cancellationToken);
                await StartEgressProxyAsync(request, idempotencyKey, cancellationToken);
            }

            await StartIfCreatedAsync(existing, cancellationToken);
            watcher.Watch(Watched(request, existing.ID));
            return new StartedStep(existing.ID, StepName(existing), Created: false);
        }

        // The credential is read at this moment and no earlier, and before anything is created, so that a secret that is not stored leaves no network or proxy behind. It goes into the container's environment and
        // nowhere else: no label, name, volume, or file holds it.
        AddSecrets(definition, await ResolveSecretsAsync(request.SecretReferences, cancellationToken));

        // The step run's network and its egress proxy come first, and the step container is created only when the proxy listens, so that no step meets a network with no way out.
        await EnsureNetworkAsync(options.OutboundNetwork, isInternal: false, WorkspaceLabels.For(options.InstallationId, null, null, idempotencyKey), cancellationToken);
        await EnsureNetworkAsync(WorkspaceLabels.StepNetwork(request.StepRunId), isInternal: true, WorkspaceLabels.For(options.InstallationId, TaskLabel(request), request.StepRunId, idempotencyKey), cancellationToken);
        await StartEgressProxyAsync(request, idempotencyKey, cancellationToken);
        await PlaceFilesAsync(request, idempotencyKey, cancellationToken);

        string id;
        try
        {
            id = (await docker.Containers.CreateContainerAsync(definition, cancellationToken)).ID;
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            // Two calls for one step run reached Docker together; the container that won is the step run's.
            var raced = await FindStepContainerAsync(request.StepRunId, cancellationToken) ?? throw new InvalidOperationException($"The name of the container of step run {request.StepRunId} is taken by something that is not it.", exception);
            await StartIfCreatedAsync(raced, cancellationToken);
            watcher.Watch(Watched(request, raced.ID));
            return new StartedStep(raced.ID, StepName(raced), Created: false);
        }

        try
        {
            await docker.Containers.StartContainerAsync(id, new ContainerStartParameters(), cancellationToken);
        }
        catch (DockerApiException)
        {
            // A container that cannot start would be found, started, and fail the same way at every retry; the retry makes a new one.
            await RemoveQuietlyAsync(id);
            throw;
        }

        watcher.Watch(Watched(request, id));
        return new StartedStep(id, WorkspaceLabels.StepContainer(request.StepRunId), Created: true);
    }

    /// <summary>From here the runtime, and nothing else, sees the container to its end: its output, its deadline, the Signal that reports it, and its removal (AD-18).</summary>
    private static WatchedStep Watched(StepContainerRequest request, string containerId) => new(request.TaskId, request.StepRunId, containerId, request.Deadline);

    private async Task<ContainerListResponse?> FindStepContainerAsync(Guid stepRunId, CancellationToken cancellationToken)
    {
        var found = await docker.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{WorkspaceLabels.Installation}={options.InstallationId}"] = true,
                        [$"{WorkspaceLabels.StepRun}={stepRunId:D}"] = true,
                        [$"{WorkspaceLabels.Role}={WorkspaceLabels.StepRole}"] = true,
                    },
                },
            },
            cancellationToken);
        return found.FirstOrDefault();
    }

    private static string StepName(ContainerListResponse container) => container.Names?.FirstOrDefault()?.TrimStart('/') ?? string.Empty;

    /// <summary>A container that was created and never started is what a crash between the two leaves; one that has run is left alone.</summary>
    private async Task StartIfCreatedAsync(ContainerListResponse container, CancellationToken cancellationToken)
    {
        if (string.Equals(container.State, "created", StringComparison.Ordinal))
        {
            await docker.Containers.StartContainerAsync(container.ID, new ContainerStartParameters(), cancellationToken);
        }
    }

    /// <summary>
    /// Makes a network of the given name, or finds the one that is there: a crash between the intent and its result leaves it, and the same name is used again. A network of the step run
    /// that has a route out would void the isolation of the step on it (AD-7), so what is found must be internal, and is never used otherwise; an outbound network that is internal would
    /// leave every proxy with no route out and nothing to name the cause, so it is refused the same way.
    /// </summary>
    private async Task EnsureNetworkAsync(string name, bool isInternal, Dictionary<string, string> labels, CancellationToken cancellationToken)
    {
        try
        {
            await RequireNetworkAsync(name, isInternal, cancellationToken);
            return;
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // Not there yet.
        }

        try
        {
            await docker.Networks.CreateNetworkAsync(new NetworksCreateParameters { Name = name, Driver = "bridge", Internal = isInternal, Labels = labels }, cancellationToken);
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            // Made by another call between the look and the creation, or by something else: what is there must be what is asked for all the same.
            await RequireNetworkAsync(name, isInternal, cancellationToken);
        }
    }

    private async Task RequireNetworkAsync(string name, bool isInternal, CancellationToken cancellationToken)
    {
        var network = await docker.Networks.InspectNetworkAsync(name, cancellationToken);
        if (isInternal && !network.Internal)
        {
            throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceNetworkNotInternal, name));
        }

        if (!isInternal && network.Internal)
        {
            throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceNetworkNotOutbound, name));
        }
    }

    private async Task CreateVolumeAsync(string name, Dictionary<string, string> labels, CancellationToken cancellationToken) =>
        await docker.Volumes.CreateAsync(new VolumesCreateParameters { Name = name, Labels = labels }, cancellationToken);

    /// <summary>Puts the context and the Methodology of the step run into volumes of their own, which the step container then mounts read-only.</summary>
    private async Task PlaceFilesAsync(StepContainerRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        var labels = WorkspaceLabels.For(options.InstallationId, request.TaskId, request.StepRunId, idempotencyKey);
        var context = WorkspaceLabels.ContextVolume(request.StepRunId);
        var methodology = WorkspaceLabels.MethodologyVolume(request.StepRunId);
        await CreateVolumeAsync(context, labels, cancellationToken);
        await CreateVolumeAsync(methodology, labels, cancellationToken);

        // A container that is created and never started is enough to fill a volume: Docker mounts the volumes to write into them. It is created
        // from the Task's image so that nothing is fetched, and with volume-nocopy so that the image's own files cannot enter a volume first.
        var helper = WorkspaceLabels.Helper(WorkspaceLabels.FillRole, request.StepRunId);
        await RemoveIfExistsAsync(helper, cancellationToken);
        var created = await docker.Containers.CreateContainerAsync(
            InertContainer(
                helper,
                request.Image.Reference,
                WorkspaceLabels.For(options.InstallationId, request.TaskId, request.StepRunId, idempotencyKey, WorkspaceLabels.FillRole),
                [VolumeMount(context, FillContextDirectory, readOnly: false), VolumeMount(methodology, FillMethodologyDirectory, readOnly: false)]),
            cancellationToken);
        try
        {
            await ExtractAsync(created.ID, FillContextDirectory, request.Context, cancellationToken);
            await ExtractAsync(created.ID, FillMethodologyDirectory, request.Methodology, cancellationToken);
        }
        finally
        {
            await RemoveQuietlyAsync(created.ID);
        }
    }

    private async Task ExtractAsync(string container, string directory, IReadOnlyList<WorkspaceFile> files, CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return;
        }

        using var archive = TarFiles.ReadOnlyFiles(files);
        await docker.Containers.ExtractArchiveToContainerAsync(container, new CopyToContainerParameters { Path = directory, CopyUIDGID = true }, archive, cancellationToken);
    }

    private async Task RemoveIfExistsAsync(string container, CancellationToken cancellationToken)
    {
        try
        {
            await docker.Containers.RemoveContainerAsync(container, HelperRemoval, cancellationToken);
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // Nothing was left by an earlier call.
        }
    }

    /// <summary>Removes an inert helper once it has done its work; a failure to remove it must not hide how the work went, and the next call replaces it by name.</summary>
    private async Task RemoveQuietlyAsync(string container)
    {
        try
        {
            await docker.Containers.RemoveContainerAsync(container, HelperRemoval, CancellationToken.None);
        }
        catch (Exception exception) when (exception is DockerApiException or HttpRequestException or IOException or TaskCanceledException)
        {
            // Left for the next call, which removes it by name.
        }
    }
}
