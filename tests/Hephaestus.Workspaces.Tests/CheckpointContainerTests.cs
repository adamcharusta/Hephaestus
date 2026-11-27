using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using Docker.DotNet;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Aliases;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces.Tests;

/// <summary>
/// Story 3.6, AD-6, AD-9, FR-20, AR-50: what the runtime asks Docker for when it checkpoints a step, and what it does with what comes back, against an Engine in memory. The container is the tools image's, has no network and no
/// credential, and only reads the Task volume; the result is read as exactly one regular file of bounded size under the name the runtime chose, and anything else is an infrastructure error from which nothing is used.
/// </summary>
public sealed class CheckpointContainerTests : IDisposable
{
    private static readonly Guid TaskId = Guid.Parse("018f4b6e-0000-7000-8000-000000000001");
    private static readonly Guid StepRunId = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");
    private const string Branch = "hephaestus/12-checkpoint";
    private const string Key = "task:g4:checkpoint:0";
    private const string ToolsImage = "registry.test/hephaestus-tools:test";

    private static readonly string Parent = new('d', 40);
    private static readonly string Tip = new('e', 40);
    private static readonly string Name = $"{TaskId:D}/{StepRunId:D}-checkpoint.bundle";

    private static readonly CheckpointCommit Commit = CheckpointCommit.Compose(
        new CommitIdentity("hephaestus-test[bot]", "hephaestus-test[bot]@users.noreply.github.com"), Language.English, StepKind.Implementation, TaskId, StepRunId, Agent.ClaudeCode, "opus5.5", Effort.High);

    private readonly string _bundles = Directory.CreateTempSubdirectory("hephaestus-bundles-").FullName;
    private readonly DockerDouble _docker = new();
    private readonly byte[] _content = [.. "a bundle of a repository"u8];

    public CheckpointContainerTests()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:" + new string('c', 64), "amd64");
        _docker.Inspector = (_, _) => Task.FromResult(DockerDouble.Inspected("exited", 0));
        _docker.ResultArchive = (_, _) => DockerDouble.Archive(ResultTar(ResultBundle()));
    }

    public void Dispose()
    {
        Directory.Delete(_bundles, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static byte[] ResultBundle(string? branch = null, string? tip = null, string[]? prerequisites = null, string? extraHead = null, string capabilities = "")
    {
        var text = new StringBuilder("# v2 git bundle\n");
        foreach (var prerequisite in prerequisites ?? [Parent])
        {
            text.Append('-').Append(prerequisite).Append(" a commit\n");
        }

        text.Append(tip ?? Tip).Append(" refs/heads/").Append(branch ?? Branch).Append('\n');
        if (extraHead is not null)
        {
            text.Append(extraHead).Append(" refs/heads/other\n");
        }

        text.Append(capabilities).Append("\nPACK-not-checked");
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static byte[] ResultTar(byte[] content, string name = "result.bundle", TarEntryType type = TarEntryType.RegularFile, string? linkName = null, int extraEntries = 0)
    {
        var archive = new MemoryStream();
        using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
        {
            var entry = new PaxTarEntry(type, name);
            if (type == TarEntryType.RegularFile)
            {
                entry.DataStream = new MemoryStream(content);
            }

            if (linkName is not null)
            {
                entry.LinkName = linkName;
            }

            writer.WriteEntry(entry);
            for (var extra = 0; extra < extraEntries; extra++)
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"extra{extra}") { DataStream = new MemoryStream([1]) });
            }
        }

        return archive.ToArray();
    }

    private WorkspaceRuntimeOptions Options(Func<WorkspaceRuntimeOptions, WorkspaceRuntimeOptions>? change = null)
    {
        var options = new WorkspaceRuntimeOptions("test-installation", new FakeVendor().Lock, AgentArchitecture.X64)
        {
            ToolsImage = ToolsImage,
            BundlesDirectory = _bundles,
            CheckoutPollInterval = TimeSpan.FromMilliseconds(5),
            CheckpointLimit = TimeSpan.FromSeconds(30),
        };
        return change is null ? options : change(options);
    }

    private BundleReference WriteParent(string name = "task/step-checkout.bundle", byte[]? content = null)
    {
        var path = Path.Combine(_bundles, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content ?? _content);
        return new BundleReference(name, Convert.ToHexStringLower(SHA256.HashData(content ?? _content)), Parent);
    }

    private static CheckpointRequest Request(BundleReference parent, StepKind kind = StepKind.Implementation) => new(TaskId, StepRunId, kind, Branch, parent, Commit);

    private Task<CheckpointResult> CheckpointAsync(BundleReference parent, WorkspaceRuntimeOptions? options = null) =>
        new DockerWorkspaceRuntime(_docker.Client, options ?? Options(), new RecordingWatcher()).CheckpointAsync(Request(parent), Key, TestContext.Current.CancellationToken);

    private async Task<Docker.DotNet.Models.CreateContainerParameters> CheckpointedAsync()
    {
        await CheckpointAsync(WriteParent());
        return Assert.Single(_docker.Created);
    }

    private async Task<WorkspaceRuntimeException> RefusedAsync()
    {
        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckpointAsync(WriteParent()));
        Assert.False(File.Exists(Path.Combine(_bundles, Name.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Empty(Directory.EnumerateFiles(_bundles, ".checkpoint-*", SearchOption.AllDirectories));
        Assert.Contains($"id-hephaestus-checkpoint-{StepRunId}", _docker.Removed);
        return failure;
    }

    [Fact]
    public async Task The_checkpoint_container_is_the_tools_image_as_the_step_user_with_no_network_and_every_capability_dropped()
    {
        var container = await CheckpointedAsync();

        Assert.Equal($"hephaestus-checkpoint-{StepRunId}", container.Name);
        Assert.Equal(ToolsImage, container.Image);
        Assert.Equal("10001:10001", container.User);
        Assert.Equal("none", container.HostConfig!.NetworkMode);
        Assert.Equal(["ALL"], container.HostConfig.CapDrop);
        Assert.Equal(["no-new-privileges"], container.HostConfig.SecurityOpt);
        Assert.False(container.HostConfig.Privileged);
        Assert.False(container.HostConfig.PublishAllPorts);
        Assert.Equal(512, container.HostConfig.PidsLimit);
        Assert.Equal(1024L * 1024 * 1024, container.HostConfig.Memory);
        Assert.Equal(container.HostConfig.Memory, container.HostConfig.MemorySwap);
        Assert.Equal(1_000_000_000L, container.HostConfig.NanoCPUs);
    }

    [Fact]
    public async Task The_checkpoint_container_carries_the_labels_of_the_convention_and_its_role()
    {
        var container = await CheckpointedAsync();

        Assert.Equal("test-installation", container.Labels!["hephaestus.installation"]);
        Assert.Equal(TaskId.ToString(), container.Labels!["hephaestus.task"]);
        Assert.Equal(StepRunId.ToString(), container.Labels!["hephaestus.step-run"]);
        Assert.Equal("checkpoint", container.Labels!["hephaestus.role"]);
        Assert.Equal(Key, container.Labels!["hephaestus.effect"]);
    }

    [Fact]
    public async Task The_only_mount_is_the_task_volume_read_only_without_copying_from_the_image_and_no_host_path_is_bound()
    {
        var container = await CheckpointedAsync();

        var mount = Assert.Single(container.HostConfig!.Mounts!);
        Assert.Equal("volume", mount.Type);
        Assert.Equal($"hephaestus-ws-{TaskId}", mount.Source);
        Assert.Equal("/workspace", mount.Target);
        Assert.True(mount.ReadOnly);
        Assert.True(mount.VolumeOptions!.NoCopy);
        Assert.True(container.HostConfig.Binds is null or { Count: 0 });
        Assert.Empty(_docker.Volumes);
        Assert.Empty(_docker.Networks);
    }

    [Fact]
    public async Task The_environment_holds_the_branch_the_parent_and_what_core_composed_and_no_credential()
    {
        var container = await CheckpointedAsync();

        Assert.Equal(
            [
                $"HEPHAESTUS_BRANCH={Branch}",
                $"HEPHAESTUS_TIP={Parent}",
                "HEPHAESTUS_NAME=hephaestus-test[bot]",
                "HEPHAESTUS_EMAIL=hephaestus-test[bot]@users.noreply.github.com",
                $"HEPHAESTUS_MESSAGE={Commit.Message}",
                "HOME=/tmp",
            ],
            container.Env);
        Assert.All(container.Env!, variable => Assert.DoesNotMatch("(?i)token|secret|key|password|credential|github", variable.Split('=')[0]));
        Assert.Equal(["sh", "-c", CheckpointScript.Text, "checkpoint", "/workspace", "/checkpoint/base.bundle", "/checkpoint/repository", "/checkpoint/result.bundle"], container.Entrypoint);
        Assert.Empty(container.Cmd!);
    }

    [Fact]
    public async Task The_base_bundle_is_the_one_file_the_container_is_given_and_is_owned_by_the_step_user()
    {
        await CheckpointedAsync();

        var extraction = Assert.Single(_docker.Extractions);
        using var reader = new TarReader(new MemoryStream(extraction.Tar));
        var directory = reader.GetNextEntry()!;
        Assert.Equal(TarEntryType.Directory, directory.EntryType);
        Assert.Equal("checkpoint", directory.Name.TrimEnd('/'));
        Assert.Equal(10001, directory.Uid);
        var file = reader.GetNextEntry()!;
        Assert.Equal("checkpoint/base.bundle", file.Name);
        Assert.Equal(10001, file.Uid);
        using var content = new MemoryStream();
        file.DataStream!.CopyTo(content);
        Assert.Equal(_content, content.ToArray());
        Assert.Null(reader.GetNextEntry());
    }

    [Fact]
    public async Task A_changed_tree_gives_a_bundle_reference_with_its_path_checksum_tip_and_prerequisite_and_the_file_is_in_place()
    {
        var result = await CheckpointAsync(WriteParent());

        var bundle = result.Bundle!;
        Assert.False(result.IsUnchanged);
        Assert.Equal(Name, bundle.Path);
        Assert.Equal(Tip, bundle.Tip);
        Assert.Equal(Parent, bundle.Prerequisite);
        var file = Path.Combine(_bundles, TaskId.ToString(), $"{StepRunId}-checkpoint.bundle");
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))), bundle.Sha256);
        Assert.Equal(ResultBundle(), File.ReadAllBytes(file));
        Assert.Equal(["/checkpoint/result.bundle"], _docker.Statted);
    }

    [Fact]
    public async Task The_container_is_started_once_and_removed_when_it_ends_and_the_base_bundle_is_deleted_and_the_result_is_not()
    {
        await CheckpointAsync(WriteParent());

        Assert.Equal([$"id-hephaestus-checkpoint-{StepRunId}"], _docker.Started);
        Assert.Contains($"id-hephaestus-checkpoint-{StepRunId}", _docker.Removed);
        Assert.True(_docker.Removals.Last().Force);
        Assert.False(File.Exists(Path.Combine(_bundles, "task", "step-checkout.bundle")));
        Assert.True(File.Exists(Path.Combine(_bundles, TaskId.ToString(), $"{StepRunId}-checkpoint.bundle")));
        Assert.Empty(Directory.EnumerateFiles(_bundles, ".checkpoint-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_bundle_a_crash_left_for_the_step_run_is_replaced_by_the_new_one()
    {
        var path = Path.Combine(_bundles, TaskId.ToString(), $"{StepRunId}-checkpoint.bundle");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "an earlier attempt");

        await CheckpointAsync(WriteParent());

        Assert.Equal(ResultBundle(), File.ReadAllBytes(path));
    }

    [Fact]
    public async Task A_container_a_crash_left_for_the_step_run_is_replaced_by_name()
    {
        await CheckpointedAsync();

        Assert.True(_docker.Calls.IndexOf($"remove hephaestus-checkpoint-{StepRunId}") < _docker.Calls.IndexOf($"create container hephaestus-checkpoint-{StepRunId}"));
    }

    [Fact]
    public async Task An_unchanged_tree_gives_no_bundle_reads_nothing_from_the_container_and_leaves_no_file()
    {
        _docker.Inspector = (_, _) => Task.FromResult(DockerDouble.Inspected("exited", CheckpointScript.Unchanged));
        var stale = Path.Combine(_bundles, TaskId.ToString(), $"{StepRunId}-checkpoint.bundle");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "an earlier attempt");

        var result = await CheckpointAsync(WriteParent());

        Assert.True(result.IsUnchanged);
        Assert.Null(result.Bundle);
        Assert.Empty(_docker.Statted);
        Assert.False(File.Exists(stale));
        Assert.False(File.Exists(Path.Combine(_bundles, "task", "step-checkout.bundle")));
        Assert.Contains($"id-hephaestus-checkpoint-{StepRunId}", _docker.Removed);
    }

    [Fact]
    public async Task An_unchanged_tree_needs_no_directory_for_a_result_that_was_never_made()
    {
        _docker.Inspector = (_, _) => Task.FromResult(DockerDouble.Inspected("exited", CheckpointScript.Unchanged));
        var parent = WriteParent("elsewhere/step-checkout.bundle");

        var result = await CheckpointAsync(parent);

        Assert.True(result.IsUnchanged);
        Assert.False(Directory.Exists(Path.Combine(_bundles, TaskId.ToString())));
    }

    [Theory]
    [InlineData("running")]
    [InlineData("restarting")]
    [InlineData("paused")]
    public async Task A_step_container_that_still_runs_on_the_volume_is_no_tree_to_read_and_the_base_bundle_is_deleted(string state)
    {
        _docker.Existing.Add(new Docker.DotNet.Models.ContainerListResponse { ID = "step-container", State = state });
        var parent = WriteParent();

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckpointAsync(parent));

        Assert.Equal(MessageIds.WorkspaceCheckpointStepRunning, failure.Info.Id);
        Assert.Equal<object[]>([StepRunId.ToString()], failure.Info.Arguments);
        Assert.Empty(_docker.Created);
        Assert.False(File.Exists(Path.Combine(_bundles, "task", "step-checkout.bundle")));
        Assert.Empty(_docker.Stops);
    }

    [Theory]
    [InlineData("exited")]
    [InlineData("created")]
    public async Task A_step_container_that_has_ended_or_never_started_is_no_obstacle(string state)
    {
        _docker.Existing.Add(new Docker.DotNet.Models.ContainerListResponse { ID = "step-container", State = state });

        var result = await CheckpointAsync(WriteParent());

        Assert.False(result.IsUnchanged);
    }

    [Fact]
    public async Task A_checkpoint_that_is_cancelled_while_the_file_is_read_leaves_nothing_on_the_data_volume()
    {
        using var cancel = new CancellationTokenSource();
        _docker.ResultArchive = (_, _) =>
        {
            cancel.Cancel();
            return DockerDouble.Archive(ResultTar(ResultBundle()));
        };
        var runtime = new DockerWorkspaceRuntime(_docker.Client, Options(), new RecordingWatcher());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.CheckpointAsync(Request(WriteParent()), Key, cancel.Token));

        Assert.False(File.Exists(Path.Combine(_bundles, Name.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Empty(Directory.EnumerateFiles(_bundles, ".checkpoint-*", SearchOption.AllDirectories));
        Assert.Contains($"id-hephaestus-checkpoint-{StepRunId}", _docker.Removed);
    }

    [Theory]
    [InlineData(CheckoutScript.BundleNotVerified, MessageIds.WorkspaceCheckpointBundleNotVerified)]
    [InlineData(CheckoutScript.BundleHeads, MessageIds.WorkspaceCheckpointBundleHeads)]
    [InlineData(CheckoutScript.BundleTip, MessageIds.WorkspaceCheckpointBundleTip)]
    public async Task A_checkpoint_that_refuses_the_base_bundle_says_which_way_in_a_message_of_its_own(int exitCode, string expected)
    {
        _docker.Inspector = (_, _) => Task.FromResult(DockerDouble.Inspected("exited", exitCode));
        var parent = WriteParent();

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckpointAsync(parent));

        Assert.Equal(expected, failure.Info.Id);
        Assert.Equal<object[]>([parent.Path], failure.Info.Arguments);
        Assert.Empty(_docker.Statted);
    }

    [Fact]
    public async Task Any_other_failure_names_the_exit_code_and_the_last_of_what_the_container_printed()
    {
        _docker.Inspector = (_, _) => Task.FromResult(DockerDouble.Inspected("exited", 128));
        _docker.Logs = (_, _) => Task.FromResult(DockerDouble.Output((2, new string('x', 600) + "fatal: bad object\n")));

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckpointAsync(WriteParent()));

        Assert.Equal(MessageIds.WorkspaceCheckpointFailed, failure.Info.Id);
        Assert.Equal("128", failure.Info.Arguments[0]);
        var printed = (string)failure.Info.Arguments[1];
        Assert.EndsWith("fatal: bad object", printed, StringComparison.Ordinal);
        Assert.Equal(500, printed.Length);
        Assert.Empty(_docker.Statted);
    }

    [Fact]
    public async Task A_checkpoint_that_does_not_end_in_time_is_removed_by_force_and_never_stopped_by_the_runtime()
    {
        _docker.Inspector = (_, _) => Task.FromResult(DockerDouble.Inspected("running"));

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckpointAsync(WriteParent(), Options(o => o with { CheckpointLimit = TimeSpan.FromMilliseconds(40) })));

        Assert.Equal(MessageIds.WorkspaceCheckpointTimedOut, failure.Info.Id);
        Assert.Empty(_docker.Stops);
        Assert.Contains($"id-hephaestus-checkpoint-{StepRunId}", _docker.Removed);
        Assert.True(_docker.Removals.Last().Force);
    }

    [Fact]
    public async Task A_base_bundle_whose_checksum_is_not_the_one_written_is_refused_before_the_container_gets_it_and_is_deleted()
    {
        var parent = WriteParent();
        File.WriteAllBytes(Path.Combine(_bundles, "task", "step-checkout.bundle"), [1, 2, 3]);

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckpointAsync(parent));

        Assert.Equal(MessageIds.WorkspaceCheckpointBundleChecksum, failure.Info.Id);
        Assert.Empty(_docker.Extractions);
        Assert.Empty(_docker.Started);
        Assert.False(File.Exists(Path.Combine(_bundles, "task", "step-checkout.bundle")));
    }

    [Fact]
    public async Task A_base_bundle_that_is_missing_or_larger_than_the_bound_is_refused_before_anything_is_asked_of_docker()
    {
        var missing = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckpointAsync(new BundleReference("task/missing.bundle", new string('0', 64), Parent)));
        Assert.Equal(MessageIds.WorkspaceCheckpointBundleFile, missing.Info.Id);

        var large = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckpointAsync(WriteParent(), Options(o => o with { MaximumBundleBytes = 4 })));
        Assert.Equal(MessageIds.WorkspaceCheckpointBundleSize, large.Info.Id);
        Assert.Equal<object[]>(["task/step-checkout.bundle", "4"], large.Info.Arguments);

        Assert.Empty(_docker.Calls);
    }

    [Fact]
    public async Task A_task_whose_volume_does_not_exist_gets_no_checkpoint()
    {
        _docker.TaskVolumeExists = false;
        var parent = WriteParent();

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckpointAsync(parent));

        Assert.Equal(MessageIds.WorkspaceTaskVolumeMissing, failure.Info.Id);
        Assert.Empty(_docker.Created);
        Assert.False(File.Exists(Path.Combine(_bundles, "task", "step-checkout.bundle")));
    }

    [Fact]
    public async Task A_container_that_ended_well_and_left_no_file_is_an_error_and_not_an_unchanged_tree()
    {
        _docker.ResultArchive = (_, _) => Task.FromException<Docker.DotNet.Models.ContainerArchiveResponse>(DockerDouble.NotFound());

        var failure = await RefusedAsync();

        Assert.Equal(MessageIds.WorkspaceCheckpointResultMissing, failure.Info.Id);
    }

    [Theory]
    [InlineData(TarEntryType.SymbolicLink, "result.bundle", "/etc/passwd", 0)]
    [InlineData(TarEntryType.HardLink, "result.bundle", "other", 0)]
    [InlineData(TarEntryType.Directory, "result.bundle", null, 0)]
    [InlineData(TarEntryType.RegularFile, "../result.bundle", null, 0)]
    [InlineData(TarEntryType.RegularFile, "/result.bundle", null, 0)]
    [InlineData(TarEntryType.RegularFile, "checkpoint/result.bundle", null, 0)]
    [InlineData(TarEntryType.RegularFile, "another.bundle", null, 0)]
    [InlineData(TarEntryType.RegularFile, "result.bundle", null, 1)]
    public async Task An_archive_with_a_link_a_directory_a_path_another_name_or_an_extra_entry_is_an_infrastructure_error_and_nothing_is_used(TarEntryType type, string name, string? linkName, int extra)
    {
        _docker.ResultArchive = (_, _) => DockerDouble.Archive(ResultTar(ResultBundle(), name, type, linkName, extra));

        var failure = await RefusedAsync();

        Assert.Equal(MessageIds.WorkspaceCheckpointResultArchive, failure.Info.Id);
        Assert.Equal<object[]>(["result.bundle"], failure.Info.Arguments);
    }

    [Fact]
    public async Task An_archive_that_is_not_a_tar_is_an_infrastructure_error()
    {
        _docker.ResultArchive = (_, _) => DockerDouble.Archive([.. Enumerable.Repeat((byte)'x', 2000)]);

        var failure = await RefusedAsync();

        Assert.Equal(MessageIds.WorkspaceCheckpointResultArchive, failure.Info.Id);
    }

    [Fact]
    public async Task An_empty_archive_and_an_archive_without_a_stream_are_infrastructure_errors()
    {
        _docker.ResultArchive = (_, _) => DockerDouble.Archive([]);
        Assert.Equal(MessageIds.WorkspaceCheckpointResultArchive, (await RefusedAsync()).Info.Id);

        _docker.ResultArchive = (_, _) => Task.FromResult(new Docker.DotNet.Models.ContainerArchiveResponse { Stat = new Docker.DotNet.Models.ContainerPathStatResponse(), Stream = null });
        Assert.Equal(MessageIds.WorkspaceCheckpointResultArchive, (await RefusedAsync()).Info.Id);
    }

    [Fact]
    public async Task A_file_over_the_size_bound_is_an_infrastructure_error_and_is_not_read()
    {
        _docker.ResultArchive = (_, _) => DockerDouble.Archive(ResultTar([.. ResultBundle(), .. new byte[200]]));

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckpointAsync(WriteParent(), Options(o => o with { MaximumBundleBytes = 100 })));

        Assert.Equal(MessageIds.WorkspaceCheckpointResultSize, failure.Info.Id);
        Assert.Equal<object[]>(["100"], failure.Info.Arguments);
        Assert.False(File.Exists(Path.Combine(_bundles, Name.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Empty(Directory.EnumerateFiles(_bundles, ".checkpoint-*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("other-branch", null, null, null)]
    [InlineData(null, "dddddddddddddddddddddddddddddddddddddddd", null, null)]
    [InlineData(null, null, "ffffffffffffffffffffffffffffffffffffffff", null)]
    [InlineData(null, null, null, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    public async Task A_bundle_that_is_not_one_head_on_the_branch_with_the_parent_as_its_one_prerequisite_is_refused(string? branch, string? tip, string? prerequisite, string? extraHead)
    {
        _docker.ResultArchive = (_, _) => DockerDouble.Archive(ResultTar(ResultBundle(branch, tip, prerequisite is null ? null : [prerequisite], extraHead)));

        var failure = await RefusedAsync();

        Assert.Equal(MessageIds.WorkspaceCheckpointResultHeader, failure.Info.Id);
        Assert.Equal<object[]>([Branch], failure.Info.Arguments);
    }

    [Fact]
    public async Task A_bundle_with_no_prerequisite_or_two_or_that_is_not_a_bundle_is_refused()
    {
        _docker.ResultArchive = (_, _) => DockerDouble.Archive(ResultTar(ResultBundle(prerequisites: [])));
        Assert.Equal(MessageIds.WorkspaceCheckpointResultHeader, (await RefusedAsync()).Info.Id);

        _docker.ResultArchive = (_, _) => DockerDouble.Archive(ResultTar(ResultBundle(prerequisites: [Parent, new string('9', 40)])));
        Assert.Equal(MessageIds.WorkspaceCheckpointResultHeader, (await RefusedAsync()).Info.Id);

        _docker.ResultArchive = (_, _) => DockerDouble.Archive(ResultTar([.. "just some text"u8]));
        Assert.Equal(MessageIds.WorkspaceCheckpointResultHeader, (await RefusedAsync()).Info.Id);
    }

    [Fact]
    public async Task A_docker_failure_while_reading_the_file_is_not_taken_for_a_missing_file()
    {
        _docker.ResultArchive = (_, _) => Task.FromException<Docker.DotNet.Models.ContainerArchiveResponse>(new DockerApiException(System.Net.HttpStatusCode.InternalServerError, "engine failed"));

        await Assert.ThrowsAsync<DockerApiException>(() => CheckpointAsync(WriteParent()));

        Assert.Contains($"id-hephaestus-checkpoint-{StepRunId}", _docker.Removed);
    }

    [Fact]
    public async Task The_runtime_makes_no_checkpoint_for_a_request_it_cannot_read()
    {
        var runtime = new DockerWorkspaceRuntime(_docker.Client, Options(), new RecordingWatcher());

        await Assert.ThrowsAsync<ArgumentNullException>(() => runtime.CheckpointAsync(null!, Key, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_options_give_a_checkpoint_a_limit_of_its_own()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), new WorkspaceRuntimeOptions("x", new FakeVendor().Lock, AgentArchitecture.X64).CheckpointLimit);
    }
}
