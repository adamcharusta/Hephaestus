using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using Message = Hephaestus.Core.Localization.Message;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces;

/// <summary>
/// The Docker runtime (AD-6, AD-7, AD-18, Story 3.2). It creates the hardened container of a step and starts it without waiting for the step to
/// end, creates and prepares the Task's volume, and resolves a Project's image. It is the only code that talks to the Docker socket, and it
/// writes no journal: the loop does, and passes the idempotency key of each effect.
/// </summary>
public sealed partial class DockerWorkspaceRuntime(IDockerClient docker, WorkspaceRuntimeOptions options) : IWorkspaceRuntime
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
            await StartIfCreatedAsync(existing, cancellationToken);
            return new StartedStep(existing.ID, StepName(existing), Created: false);
        }

        await EnsureWorkspaceNetworkAsync(idempotencyKey, cancellationToken);
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

        return new StartedStep(id, WorkspaceLabels.StepContainer(request.StepRunId), Created: true);
    }

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
    /// The workspace network: a Docker internal network, which has no route out (AD-7). Its only exit, the egress proxy, joins it in a later story.
    /// </summary>
    private async Task EnsureWorkspaceNetworkAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            await RequireInternalAsync(cancellationToken);
            return;
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // Not there yet.
        }

        try
        {
            await docker.Networks.CreateNetworkAsync(
                new NetworksCreateParameters
                {
                    Name = options.WorkspaceNetwork,
                    Driver = "bridge",
                    Internal = true,
                    Labels = WorkspaceLabels.For(options.InstallationId, null, null, idempotencyKey),
                },
                cancellationToken);
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            // Made by another call between the look and the creation, or by something else: what is there must be internal all the same.
            await RequireInternalAsync(cancellationToken);
        }
    }

    /// <summary>A network of that name that has a route out would void the isolation of every step on it (AD-7), so it is never used.</summary>
    private async Task RequireInternalAsync(CancellationToken cancellationToken)
    {
        var network = await docker.Networks.InspectNetworkAsync(options.WorkspaceNetwork, cancellationToken);
        if (!network.Internal)
        {
            throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceNetworkNotInternal, options.WorkspaceNetwork));
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
