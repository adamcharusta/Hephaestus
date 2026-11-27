using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using Docker.DotNet;
using Docker.DotNet.Models;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Workspaces;
using Message = Hephaestus.Core.Localization.Message;

namespace Hephaestus.Workspaces;

public sealed partial class DockerWorkspaceRuntime
{
    /// <summary>The directory of the checkout container's own file system the bundle is placed in, and the file; the container never mounts the data volume.</summary>
    private const string CheckoutDirectory = "checkout";
    private const string CheckoutBundleFile = "task.bundle";
    private const string CheckoutTemporaryDirectory = "/tmp";

    public async Task CheckoutAsync(CheckoutRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.BundlesDirectory));
        var source = Path.GetFullPath(Path.Combine(directory, request.Bundle.Path.Replace('/', Path.DirectorySeparatorChar)));
        try
        {
            VerifyBundleFile(request.Bundle, directory, source);
            await RequireTaskVolumeAsync(request.TaskId, cancellationToken);
            await EnsureToolsImageAsync(cancellationToken);

            // The container of a step run that a crash left is replaced: the checkout empties the workspace first, so doing it again makes the same workspace.
            var name = WorkspaceLabels.Checkout(request.StepRunId);
            await RemoveIfExistsAsync(name, cancellationToken);
            string id;
            try
            {
                id = (await docker.Containers.CreateContainerAsync(CheckoutContainer(request, idempotencyKey), cancellationToken)).ID;
            }
            catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceToolsImageUnavailable, options.ToolsImage, exception.Message), exception);
            }

            try
            {
                // The bundle is the one file the container is given, placed in its own file system: it mounts nothing of the data volume, and no credential reaches it. The checksum is taken
                // while the file is copied into the archive, so the bytes that were checked are the bytes that are sent.
                var (archive, checksum) = TarFiles.OwnedFile(CheckoutDirectory, CheckoutBundleFile, source, Path.Combine(directory, $".checkout-{request.StepRunId:D}.tar"), StepContainerLayout.UserId, StepContainerLayout.GroupId);
                using (archive)
                {
                    if (!string.Equals(checksum, request.Bundle.Sha256, StringComparison.Ordinal))
                    {
                        throw BundleRefused(MessageIds.WorkspaceCheckoutBundleChecksum, request.Bundle);
                    }

                    await docker.Containers.ExtractArchiveToContainerAsync(id, new CopyToContainerParameters { Path = "/", CopyUIDGID = true }, archive, cancellationToken);
                }

                await docker.Containers.StartContainerAsync(id, new ContainerStartParameters(), cancellationToken);
                await WaitForCheckoutAsync(id, request.Bundle, cancellationToken);
            }
            finally
            {
                await RemoveQuietlyAsync(id);
            }
        }
        finally
        {
            // A bundle is read once: whatever came of the checkout, the file is not left on the data volume, and the next attempt asks the gateway for a new one. What is deleted is a
            // path inside the bundles directory, and when it is a link, the link and not what it points to.
            if (source.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                try
                {
                    File.Delete(source);
                }
                catch (DirectoryNotFoundException)
                {
                    // There was no file to delete, and nowhere for one to be.
                }
            }
        }
    }

    /// <summary>The Task's volume is made before its first step (<see cref="EnsureTaskVolumeAsync"/>): a name that no volume has would be made by Docker, without the labels and outside the journal.</summary>
    private async Task RequireTaskVolumeAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var name = WorkspaceLabels.TaskVolume(taskId);
        try
        {
            await docker.Volumes.InspectAsync(name, cancellationToken);
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceTaskVolumeMissing, name), exception);
        }
    }

    /// <summary>
    /// The definition of a checkout container (AD-6, AR-34): the <c>hephaestus-tools</c> image, user <c>10001:10001</c> so that what it creates is the step user's, no network, every capability dropped,
    /// <c>no-new-privileges</c>, a process limit, limits of its own, and exactly one mount, the Task volume without copying from the image. Its environment holds the branch and the tip and nothing else: no credential.
    /// </summary>
    private CreateContainerParameters CheckoutContainer(CheckoutRequest request, string idempotencyKey) => new()
    {
        Name = WorkspaceLabels.Checkout(request.StepRunId),
        Image = options.ToolsImage,
        User = StepContainerLayout.User,
        Entrypoint = ["sh", "-c", CheckoutScript.Text, "checkout", StepContainerLayout.Workspace, $"/{CheckoutDirectory}/{CheckoutBundleFile}"],
        Cmd = [],
        Env =
        [
            $"HEPHAESTUS_BRANCH={request.Branch}",
            $"HEPHAESTUS_TIP={request.Bundle.Tip}",
            $"HOME={CheckoutTemporaryDirectory}",
        ],
        WorkingDir = "/",
        Labels = WorkspaceLabels.For(options.InstallationId, request.TaskId, request.StepRunId, idempotencyKey, WorkspaceLabels.CheckoutRole),
        HostConfig = new HostConfig
        {
            CapDrop = ["ALL"],
            SecurityOpt = ["no-new-privileges"],
            Privileged = false,
            PublishAllPorts = false,
            PidsLimit = StepContainerLayout.ProcessLimit,
            Memory = options.CheckoutMemoryBytes,
            MemorySwap = options.CheckoutMemoryBytes,
            NanoCPUs = options.CheckoutCpuMillis * 1_000_000L,
            NetworkMode = "none",
            RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.No },
            Mounts = [VolumeMount(WorkspaceLabels.TaskVolume(request.TaskId), StepContainerLayout.Workspace, readOnly: false)],
            Tmpfs = new Dictionary<string, string> { [CheckoutTemporaryDirectory] = $"rw,nosuid,nodev,size=64m,uid={StepContainerLayout.UserId},gid={StepContainerLayout.GroupId},mode=0700" },
        },
    };

    /// <summary>
    /// The bundle's file, before it is read: under the bundles directory, a regular file and not a link, and no larger than the bound. Its checksum is checked as it is copied (AD-9): a file that is not
    /// what the reference says is not handed to anything.
    /// </summary>
    private void VerifyBundleFile(BundleReference bundle, string directory, string path)
    {
        if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw BundleRefused(MessageIds.WorkspaceCheckoutBundleLocation, bundle);
        }

        var info = new FileInfo(path);
        if (!info.Exists || info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw BundleRefused(MessageIds.WorkspaceCheckoutBundleFile, bundle);
        }

        if (info.Length > options.MaximumBundleBytes)
        {
            throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceCheckoutBundleSize, bundle.Path, options.MaximumBundleBytes.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static WorkspaceRuntimeException BundleRefused(string id, BundleReference bundle) => new(new Message(id, bundle.Path));

    /// <summary>The checkout runs from a registry image the first time: a machine that lacks it fetches it, and one that has it, built here or fetched before, uses it.</summary>
    private async Task EnsureToolsImageAsync(CancellationToken cancellationToken)
    {
        if (await TryInspectAsync(options.ToolsImage, cancellationToken) is not null)
        {
            return;
        }

        var failures = new List<string>();
        try
        {
            await docker.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = WithTag(options.ToolsImage), Platform = $"linux/{PlatformArchitecture}" },
                null,
                new SynchronousProgress(message =>
                {
                    if (message.Error?.Message is { Length: > 0 } error)
                    {
                        failures.Add(error);
                    }
                }),
                cancellationToken);
        }
        catch (Exception exception) when (exception is DockerApiException or HttpRequestException or IOException or TimeoutException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceToolsImageUnavailable, options.ToolsImage, exception.Message), exception);
        }

        if (failures.Count > 0)
        {
            throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceToolsImageUnavailable, options.ToolsImage, string.Join(' ', failures)));
        }

        if (await TryInspectAsync(options.ToolsImage, cancellationToken) is null)
        {
            throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceToolsImageMissing, options.ToolsImage));
        }
    }

    /// <summary>Waits for the checkout container to end. One that fails, or that does not end within the limit, is removed by force, and no step is started on what it left.</summary>
    private async Task WaitForCheckoutAsync(string container, BundleReference bundle, CancellationToken cancellationToken)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            var state = (await docker.Containers.InspectContainerAsync(container, cancellationToken)).State;
            // Only a container that ran and ended counts: one that was created and has not started, or that is dead, has made nothing.
            if (state is { Running: false, Status: "exited" or "dead" })
            {
                if (state.Status == "exited" && state.ExitCode == 0)
                {
                    return;
                }

                throw await CheckoutFailureAsync(container, bundle, state.Status == "exited" ? state.ExitCode : -1, cancellationToken);
            }

            if (waited.Elapsed >= options.CheckoutLimit)
            {
                // The caller removes the container with force, which kills it: a checkout has no output to keep and no grace to give, and only the step watcher stops a container (AD-18).
                throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceCheckoutTimedOut, ((int)options.CheckoutLimit.TotalSeconds).ToString(CultureInfo.InvariantCulture)));
            }

            await Task.Delay(options.CheckoutPollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// A checkout that ended with a failure: the three ways the script refuses a bundle have a message of their own, and any other ends with the exit code and the last of what the container printed, which
    /// holds only what git said: the container has no credential.
    /// </summary>
    private async Task<WorkspaceRuntimeException> CheckoutFailureAsync(string container, BundleReference bundle, long exitCode, CancellationToken cancellationToken)
    {
        var id = exitCode switch
        {
            CheckoutScript.BundleNotVerified => MessageIds.WorkspaceCheckoutBundleNotVerified,
            CheckoutScript.BundleHeads => MessageIds.WorkspaceCheckoutBundleHeads,
            CheckoutScript.BundleTip => MessageIds.WorkspaceCheckoutBundleTip,
            _ => null,
        };
        if (id is not null)
        {
            return new WorkspaceRuntimeException(new Message(id, bundle.Path));
        }

        var printed = string.Empty;
        try
        {
            printed = (await ContainerLogs.ReadAsync(docker, container, cancellationToken)).Trim();
        }
        catch (Exception exception) when (exception is DockerApiException or HttpRequestException or IOException or TaskCanceledException)
        {
            // The exit code is what is known.
        }

        return new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceCheckoutFailed, exitCode.ToString(CultureInfo.InvariantCulture), printed.Length > 500 ? printed[^500..] : printed));
    }
}
