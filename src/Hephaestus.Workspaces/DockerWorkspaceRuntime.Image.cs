using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Workspaces;
using Message = Hephaestus.Core.Localization.Message;

namespace Hephaestus.Workspaces;

public sealed partial class DockerWorkspaceRuntime
{
    public async Task<ResolvedImage> ResolveImageAsync(string image, CancellationToken cancellationToken)
    {
        var inspected = await InspectOrFetchAsync(image, cancellationToken);

        var wanted = PlatformArchitecture;
        if (!string.Equals(inspected.Architecture, wanted, StringComparison.Ordinal))
        {
            throw new ImageNotUsableException(new Message(MessageIds.WorkspaceImageArchitecture, image, inspected.Architecture ?? "unknown", wanted));
        }

        // Docker mounts an anonymous volume for every path an image declares as one, beside the mounts Hephaestus names, so such an image cannot give the isolation promised.
        if (inspected.Config?.Volumes is { Count: > 0 } volumes)
        {
            throw new ImageNotUsableException(new Message(MessageIds.WorkspaceImageVolumes, image, string.Join(", ", volumes.Keys.Order(StringComparer.Ordinal))));
        }

        // The digest of a registry image is what a Task pins; an image that no registry knows, built on this machine, has its own id.
        var repository = RepositoryOf(image);
        var digest = inspected.RepoDigests?.FirstOrDefault(entry => entry.StartsWith(repository + "@", StringComparison.Ordinal))
            ?? inspected.RepoDigests?.FirstOrDefault(entry => entry.Contains('@', StringComparison.Ordinal));
        var reference = digest ?? inspected.ID;
        var libc = await EstablishLibcAsync(image, reference, cancellationToken);
        return new ResolvedImage(reference, digest is null ? inspected.ID : digest[(digest.IndexOf('@', StringComparison.Ordinal) + 1)..], options.Architecture, libc);
    }

    /// <summary>Reports on the thread that made the report, so that everything a pull said has been heard when the pull returns; <see cref="Progress{T}"/> posts to another thread.</summary>
    private sealed class SynchronousProgress(Action<JSONMessage> report) : IProgress<JSONMessage>
    {
        public void Report(JSONMessage value) => report(value);
    }

    private string PlatformArchitecture => options.Architecture == AgentArchitecture.Arm64 ? "arm64" : "amd64";

    /// <summary>
    /// The image as it is now: fetched when the machine lacks it, and fetched again when a tag may have moved. An image that cannot be fetched but is
    /// on the machine, such as one that was built here, is the one that is used.
    /// </summary>
    private async Task<ImageInspectResponse> InspectOrFetchAsync(string image, CancellationToken cancellationToken)
    {
        var present = await TryInspectAsync(image, cancellationToken);
        if (present is not null && image.Contains('@', StringComparison.Ordinal) && string.Equals(present.Architecture, PlatformArchitecture, StringComparison.Ordinal))
        {
            // A name that carries a digest names one image, which is here.
            return present;
        }

        var failures = new List<string>();
        try
        {
            await docker.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = WithTag(image), Platform = $"linux/{PlatformArchitecture}" },
                null,
                new SynchronousProgress(message =>
                {
                    // A pull that fails after it began says so in its stream, not in the answer to the request.
                    if (message.Error?.Message is { Length: > 0 } error)
                    {
                        failures.Add(error);
                    }
                }),
                cancellationToken);
        }
        catch (Exception exception) when (exception is DockerApiException or HttpRequestException or IOException or TimeoutException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return present ?? throw new ImageNotUsableException(new Message(MessageIds.WorkspaceImageUnavailable, image, exception.Message), exception);
        }

        if (failures.Count > 0)
        {
            return present ?? throw new ImageNotUsableException(new Message(MessageIds.WorkspaceImageUnavailable, image, string.Join(' ', failures)));
        }

        return await TryInspectAsync(image, cancellationToken)
            ?? throw new ImageNotUsableException(new Message(MessageIds.WorkspaceImageMissing, image));
    }

    private async Task<ImageInspectResponse?> TryInspectAsync(string image, CancellationToken cancellationToken)
    {
        try
        {
            return await docker.Images.InspectImageAsync(image, cancellationToken);
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>A name with no tag and no digest is <c>latest</c>: Docker's API pulls every tag of a repository when it is given a name alone.</summary>
    private static string WithTag(string image) =>
        image.Contains('@', StringComparison.Ordinal) || image.LastIndexOf(':') > image.LastIndexOf('/') ? image : image + ":latest";

    /// <summary>The name of an image without its tag or digest: <c>ghcr.io/acme/app:1.2</c> is <c>ghcr.io/acme/app</c>, and a registry port is not a tag.</summary>
    private static string RepositoryOf(string image)
    {
        var name = image.Contains('@', StringComparison.Ordinal) ? image[..image.IndexOf('@', StringComparison.Ordinal)] : image;
        var colon = name.LastIndexOf(':');
        return colon > name.LastIndexOf('/') ? name[..colon] : name;
    }

    /// <summary>
    /// Looks for the dynamic loader of each C library in the image's files, in a container that is created and never started, so that nothing of the
    /// image runs (AD-8). A musl loader wins: a musl build also runs where a compatibility layer for glibc is installed, and a glibc build does not run on musl alone.
    /// </summary>
    private async Task<CLibrary> EstablishLibcAsync(string image, string reference, CancellationToken cancellationToken)
    {
        var arm = options.Architecture == AgentArchitecture.Arm64;
        var created = await docker.Containers.CreateContainerAsync(
            InertContainer(WorkspaceLabels.Helper(WorkspaceLabels.ProbeRole, Guid.NewGuid()), reference, WorkspaceLabels.For(options.InstallationId, null, null, "image-probe", WorkspaceLabels.ProbeRole), []),
            cancellationToken);
        try
        {
            if (await ExistsAsync(created.ID, arm ? "/lib/ld-musl-aarch64.so.1" : "/lib/ld-musl-x86_64.so.1", cancellationToken))
            {
                return CLibrary.Musl;
            }

            if (await ExistsAsync(created.ID, arm ? "/lib/ld-linux-aarch64.so.1" : "/lib64/ld-linux-x86-64.so.2", cancellationToken))
            {
                return CLibrary.Glibc;
            }
        }
        finally
        {
            await RemoveQuietlyAsync(created.ID);
        }

        throw new ImageNotUsableException(new Message(MessageIds.WorkspaceImageLibc, image));
    }

    private async Task<bool> ExistsAsync(string container, string path, CancellationToken cancellationToken)
    {
        try
        {
            var response = await docker.Containers.GetArchiveFromContainerAsync(container, new ContainerPathStatParameters { Path = path }, statOnly: true, cancellationToken);
            response.Stream?.Dispose();
            return true;
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
