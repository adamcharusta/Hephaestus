using System.Globalization;
using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Workspaces;
using Message = Hephaestus.Core.Localization.Message;

namespace Hephaestus.Workspaces;

public sealed partial class DockerWorkspaceRuntime
{
    /// <summary>
    /// Where the checkpoint container's own file system holds the bundle it is given, the git directory it makes, and the one file it leaves; the runtime chose the names, and the container mounts nothing of the data volume.
    /// The directory is the user's, so the container can write beside the bundle.
    /// </summary>
    private const string CheckpointDirectory = "checkpoint";
    private const string CheckpointBaseFile = "base.bundle";
    private const string CheckpointResultFile = "result.bundle";
    private const string CheckpointGitDirectory = "repository";

    private static readonly HelperMessages CheckpointMessages = new(
        MessageIds.WorkspaceCheckpointBundleLocation,
        MessageIds.WorkspaceCheckpointBundleFile,
        MessageIds.WorkspaceCheckpointBundleSize,
        MessageIds.WorkspaceCheckpointBundleChecksum,
        MessageIds.WorkspaceCheckpointBundleNotVerified,
        MessageIds.WorkspaceCheckpointBundleHeads,
        MessageIds.WorkspaceCheckpointBundleTip,
        MessageIds.WorkspaceCheckpointFailed,
        MessageIds.WorkspaceCheckpointTimedOut);

    public async Task<CheckpointResult> CheckpointAsync(CheckpointRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.BundlesDirectory));
        var relative = $"{request.TaskId:D}/{request.StepRunId:D}-checkpoint{BundleReference.Extension}";
        var final = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
        var plan = new HelperPlan(request.TaskId, request.StepRunId, request.Parent, CheckpointDirectory, CheckpointBaseFile, WorkspaceLabels.Checkpoint(request.StepRunId), CheckpointContainer(request, idempotencyKey), CheckpointMessages, options.CheckpointLimit, [0, CheckpointScript.Unchanged])
        {
            Precondition = token => RequireStepEndedAsync(request.StepRunId, token),
        };

        var result = CheckpointResult.Unchanged;
        await RunHelperAsync(
            plan,
            async (container, exitCode) =>
            {
                if (exitCode == CheckpointScript.Unchanged)
                {
                    // No commit and no bundle: a result that an earlier attempt of the same step run left is not this one's. There may be no directory for it either.
                    DeleteBundleFile(directory, final);
                    return;
                }

                result = CheckpointResult.Made(await CollectCheckpointAsync(container, request, relative, final, directory, cancellationToken));
            },
            cancellationToken);
        return result;
    }

    /// <summary>
    /// A checkpoint reads the tree once, so it is asked for after the step has ended (AD-18): while the Agent's container still runs on the volume the tree is not a tree that ever was, and the runtime does not stop it (only the
    /// step watcher does). A container that exited is no obstacle, and neither is one that is not there.
    /// </summary>
    private async Task RequireStepEndedAsync(Guid stepRunId, CancellationToken cancellationToken)
    {
        if (await FindStepContainerAsync(stepRunId, cancellationToken) is { State: "running" or "restarting" or "paused" })
        {
            throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceCheckpointStepRunning, stepRunId.ToString("D", CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>
    /// The checkpoint container's definition (AD-9, AR-50): the tools image as the step user with no network, no credential, and every capability dropped, and the Task volume, which it only reads, as its one mount.
    /// What the script needs is in the environment: the branch, the parent, and the whole identity and message Core composed. Nothing of it is part of the script's text.
    /// </summary>
    private CreateContainerParameters CheckpointContainer(CheckpointRequest request, string idempotencyKey) => new()
    {
        Name = WorkspaceLabels.Checkpoint(request.StepRunId),
        Image = options.ToolsImage,
        User = StepContainerLayout.User,
        Entrypoint =
        [
            "sh",
            "-c",
            CheckpointScript.Text,
            "checkpoint",
            StepContainerLayout.Workspace,
            $"/{CheckpointDirectory}/{CheckpointBaseFile}",
            $"/{CheckpointDirectory}/{CheckpointGitDirectory}",
            $"/{CheckpointDirectory}/{CheckpointResultFile}",
        ],
        Cmd = [],
        Env =
        [
            $"HEPHAESTUS_BRANCH={request.Branch}",
            $"HEPHAESTUS_TIP={request.Parent.Tip}",
            $"HEPHAESTUS_NAME={request.Commit.Identity.Name}",
            $"HEPHAESTUS_EMAIL={request.Commit.Identity.Email}",
            $"HEPHAESTUS_MESSAGE={request.Commit.Message}",
            $"HOME={CheckoutTemporaryDirectory}",
        ],
        WorkingDir = "/",
        Labels = WorkspaceLabels.For(options.InstallationId, request.TaskId, request.StepRunId, idempotencyKey, WorkspaceLabels.CheckpointRole),
        HostConfig = HelperHostConfig(request.TaskId, readOnlyVolume: true),
    };

    /// <summary>
    /// Reads the one file from the stopped container (AD-9) into the bundles directory under a temporary name, checks what it is, and moves it into place. A container whose archive is not exactly one regular file of the name
    /// the runtime chose, or whose file is over the bound, or whose bundle is not one head, <c>refs/heads/&lt;Task branch&gt;</c>, on the one prerequisite that is the last pushed tip, is an infrastructure error, and nothing of it stays.
    /// The header is read as text: no git runs on what came out of a container.
    /// </summary>
    private async Task<BundleReference> CollectCheckpointAsync(string container, CheckpointRequest request, string relative, string final, string directory, CancellationToken cancellationToken)
    {
        ContainerArchiveResponse archive;
        try
        {
            archive = await docker.Containers.GetArchiveFromContainerAsync(container, new ContainerPathStatParameters { Path = $"/{CheckpointDirectory}/{CheckpointResultFile}" }, false, cancellationToken);
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceCheckpointResultMissing), exception);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(final)!);
        var temporary = Path.Combine(directory, $".{CheckpointDirectory}-{request.StepRunId:D}.tmp");
        try
        {
            using (var stream = archive.Stream ?? throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceCheckpointResultArchive, CheckpointResultFile, "no stream")))
            {
                var (outcome, checksum, detail) = await TarFiles.ReadOneFileAsync(stream, CheckpointResultFile, options.MaximumBundleBytes, temporary, cancellationToken);
                switch (outcome)
                {
                    case TarFiles.ReadOutcome.TooLarge:
                        throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceCheckpointResultSize, options.MaximumBundleBytes.ToString(CultureInfo.InvariantCulture)));
                    case TarFiles.ReadOutcome.NotOneRegularFile:
                        throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceCheckpointResultArchive, CheckpointResultFile, detail ?? string.Empty));
                }

                var tip = ReadCheckpointTip(temporary, request);
                File.Move(temporary, final, overwrite: true);
                return new BundleReference(relative, checksum!, tip, request.Parent.Tip);
            }
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>The commit the bundle carries, when its header says what was asked for: one head, the Task's branch, and one prerequisite, the parent. The commit is not the parent: a bundle that adds nothing is no checkpoint.</summary>
    private static string ReadCheckpointTip(string file, CheckpointRequest request)
    {
        BundleHeader? header;
        using (var stream = new FileStream(file, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            header = BundleHeader.TryRead(stream);
        }

        if (header is { Heads: [{ } head], Prerequisites: [{ } prerequisite] }
            && head.Reference == $"refs/heads/{request.Branch}"
            && prerequisite == request.Parent.Tip
            && head.Commit != request.Parent.Tip)
        {
            return head.Commit;
        }

        throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceCheckpointResultHeader, request.Branch));
    }
}
