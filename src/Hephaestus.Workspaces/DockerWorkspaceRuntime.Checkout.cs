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
        var plan = new HelperPlan(request.TaskId, request.StepRunId, request.Bundle, CheckoutDirectory, CheckoutBundleFile, WorkspaceLabels.Checkout(request.StepRunId), CheckoutContainer(request, idempotencyKey), CheckoutMessages, options.CheckoutLimit, [0]);
        await RunHelperAsync(plan, (_, _) => Task.CompletedTask, cancellationToken);
    }

    /// <summary>
    /// A helper that is given one bundle and ends (AD-6, AD-9): the checkout and the checkpoint. The bundle is verified as a file before anything is made, the Task's volume must exist, and the tools image is on the machine. A container
    /// of the step run that a crash left is replaced by name, because both helpers start from scratch. The bundle goes into the container's own file system as one archive entry, the container is started and waited for, and
    /// <paramref name="collect"/> reads what it left while it still exists. The container is removed and the bundle's file is deleted whatever came of it: a bundle is read once.
    /// </summary>
    private async Task RunHelperAsync(HelperPlan plan, Func<string, long, Task> collect, CancellationToken cancellationToken)
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.BundlesDirectory));
        var source = Path.GetFullPath(Path.Combine(directory, plan.Bundle.Path.Replace('/', Path.DirectorySeparatorChar)));
        try
        {
            VerifyBundleFile(plan.Bundle, directory, source, plan.Messages);
            await RequireTaskVolumeAsync(plan.TaskId, cancellationToken);
            if (plan.Precondition is not null)
            {
                await plan.Precondition(cancellationToken);
            }

            await EnsureToolsImageAsync(cancellationToken);

            await RemoveIfExistsAsync(plan.Name, cancellationToken);
            string id;
            try
            {
                id = (await docker.Containers.CreateContainerAsync(plan.Definition, cancellationToken)).ID;
            }
            catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceToolsImageUnavailable, options.ToolsImage, exception.Message), exception);
            }

            try
            {
                // The bundle is the one file the container is given, placed in its own file system: it mounts nothing of the data volume, and no credential reaches it. The checksum is taken
                // while the file is copied into the archive, so the bytes that were checked are the bytes that are sent.
                var (archive, checksum) = TarFiles.OwnedFile(plan.Directory, plan.File, source, Path.Combine(directory, $".{plan.Directory}-{plan.StepRunId:D}.tar"), StepContainerLayout.UserId, StepContainerLayout.GroupId);
                using (archive)
                {
                    if (!string.Equals(checksum, plan.Bundle.Sha256, StringComparison.Ordinal))
                    {
                        throw BundleRefused(plan.Messages.BundleChecksum, plan.Bundle);
                    }

                    await docker.Containers.ExtractArchiveToContainerAsync(id, new CopyToContainerParameters { Path = "/", CopyUIDGID = true }, archive, cancellationToken);
                }

                await docker.Containers.StartContainerAsync(id, new ContainerStartParameters(), cancellationToken);
                var exitCode = await WaitForHelperAsync(id, plan, cancellationToken);
                if (!plan.SuccessCodes.Contains(exitCode))
                {
                    throw await HelperFailureAsync(id, plan, exitCode, cancellationToken);
                }

                await collect(id, exitCode);
            }
            finally
            {
                await RemoveQuietlyAsync(id);
            }
        }
        finally
        {
            DeleteBundleFile(directory, source);
        }
    }

    /// <summary>
    /// A bundle is read once: whatever came of it, the file is not left on the data volume, and the next attempt asks the gateway for a new one. What is deleted is a path inside the bundles directory, and when it is a link, the
    /// link and not what it points to.
    /// </summary>
    private static void DeleteBundleFile(string directory, string source)
    {
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
        HostConfig = HelperHostConfig(request.TaskId, readOnlyVolume: false),
    };

    /// <summary>
    /// What the checkout and the checkpoint container share (AD-6, AD-7): no network, every capability dropped, <c>no-new-privileges</c>, a process limit, limits of their own, and exactly one mount, the Task volume without
    /// copying from the image, read-only when the helper only reads it.
    /// </summary>
    private HostConfig HelperHostConfig(Guid taskId, bool readOnlyVolume) => new()
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
        Mounts = [VolumeMount(WorkspaceLabels.TaskVolume(taskId), StepContainerLayout.Workspace, readOnlyVolume)],
        Tmpfs = new Dictionary<string, string> { [CheckoutTemporaryDirectory] = $"rw,nosuid,nodev,size=64m,uid={StepContainerLayout.UserId},gid={StepContainerLayout.GroupId},mode=0700" },
    };

    /// <summary>
    /// The bundle's file, before it is read: under the bundles directory, a regular file and not a link, and no larger than the bound. Its checksum is checked as it is copied (AD-9): a file that is not
    /// what the reference says is not handed to anything.
    /// </summary>
    private void VerifyBundleFile(BundleReference bundle, string directory, string path, HelperMessages messages)
    {
        if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw BundleRefused(messages.BundleLocation, bundle);
        }

        var info = new FileInfo(path);
        if (!info.Exists || info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw BundleRefused(messages.BundleFile, bundle);
        }

        if (info.Length > options.MaximumBundleBytes)
        {
            throw new WorkspaceRuntimeException(new Message(messages.BundleSize, bundle.Path, options.MaximumBundleBytes.ToString(CultureInfo.InvariantCulture)));
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

    /// <summary>
    /// Waits for a helper container to end and returns its exit code, or -1 for one that is dead. One that does not end within the limit is not stopped here (only the step watcher stops containers, AD-18): the caller removes
    /// it by force, which kills it, and nothing is started on what it left.
    /// </summary>
    private async Task<long> WaitForHelperAsync(string container, HelperPlan plan, CancellationToken cancellationToken)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            var state = (await docker.Containers.InspectContainerAsync(container, cancellationToken)).State;
            // Only a container that ran and ended counts: one that was created and has not started, or that is dead, has made nothing.
            if (state is { Running: false, Status: "exited" or "dead" })
            {
                return state.Status == "exited" ? state.ExitCode : -1;
            }

            if (waited.Elapsed >= plan.Limit)
            {
                throw new WorkspaceRuntimeException(new Message(plan.Messages.TimedOut, ((int)plan.Limit.TotalSeconds).ToString(CultureInfo.InvariantCulture)));
            }

            await Task.Delay(options.CheckoutPollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// A helper that ended with a failure: the three ways the script refuses a bundle have a message of their own, and any other ends with the exit code and the last of what the container printed, which
    /// holds only what git said: the container has no credential.
    /// </summary>
    private async Task<WorkspaceRuntimeException> HelperFailureAsync(string container, HelperPlan plan, long exitCode, CancellationToken cancellationToken)
    {
        var id = exitCode switch
        {
            CheckoutScript.BundleNotVerified => plan.Messages.NotVerified,
            CheckoutScript.BundleHeads => plan.Messages.Heads,
            CheckoutScript.BundleTip => plan.Messages.Tip,
            _ => null,
        };
        if (id is not null)
        {
            return new WorkspaceRuntimeException(new Message(id, plan.Bundle.Path));
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

        return new WorkspaceRuntimeException(new Message(plan.Messages.Failed, exitCode.ToString(CultureInfo.InvariantCulture), printed.Length > 500 ? printed[^500..] : printed));
    }

    /// <summary>What one of the two helpers is asked to do: the Task and step run it is for, the bundle it is given and where in its own file system it is placed, its name and definition, what it says when it fails, how long it may take, the exit codes that are not a failure, and what must hold first.</summary>
    private sealed record HelperPlan(Guid TaskId, Guid StepRunId, BundleReference Bundle, string Directory, string File, string Name, CreateContainerParameters Definition, HelperMessages Messages, TimeSpan Limit, IReadOnlyCollection<long> SuccessCodes)
    {
        /// <summary>What must hold before a container is made, checked once the Task's volume is known to exist; it throws when it does not.</summary>
        public Func<CancellationToken, Task>? Precondition { get; init; }
    }

    /// <summary>The message ids of one helper, so that each says what was not done: no step can start, or the work of a step was not checkpointed.</summary>
    private sealed record HelperMessages(string BundleLocation, string BundleFile, string BundleSize, string BundleChecksum, string NotVerified, string Heads, string Tip, string Failed, string TimedOut);

    private static readonly HelperMessages CheckoutMessages = new(
        MessageIds.WorkspaceCheckoutBundleLocation,
        MessageIds.WorkspaceCheckoutBundleFile,
        MessageIds.WorkspaceCheckoutBundleSize,
        MessageIds.WorkspaceCheckoutBundleChecksum,
        MessageIds.WorkspaceCheckoutBundleNotVerified,
        MessageIds.WorkspaceCheckoutBundleHeads,
        MessageIds.WorkspaceCheckoutBundleTip,
        MessageIds.WorkspaceCheckoutFailed,
        MessageIds.WorkspaceCheckoutTimedOut);
}
