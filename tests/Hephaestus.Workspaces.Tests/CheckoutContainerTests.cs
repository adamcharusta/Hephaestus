using System.Formats.Tar;
using System.Security.Cryptography;
using Docker.DotNet;
using Docker.DotNet.Models;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces.Tests;

/// <summary>
/// Story 3.5, AD-6, AD-9, FR-17, FR-18: what the runtime asks Docker for when it makes the checkout of a step, checked against an Engine in memory. The container is the tools image's, has no network and no credential,
/// mounts the Task volume and nothing else, is given the bundle as one archive entry, and is removed with the bundle's file when it ends.
/// </summary>
public sealed class CheckoutContainerTests : IDisposable
{
    private static readonly Guid TaskId = Guid.Parse("018f4b6e-0000-7000-8000-000000000001");
    private static readonly Guid StepRunId = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");
    private const string Branch = "hephaestus/12-fresh-checkout";
    private const string Key = "task:g4:checkout:0";
    private const string ToolsImage = "registry.test/hephaestus-tools:test";

    private readonly string _bundles = Directory.CreateTempSubdirectory("hephaestus-bundles-").FullName;
    private readonly DockerDouble _docker = new();
    private readonly byte[] _content = [.. "a bundle of a repository"u8];

    public CheckoutContainerTests()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:" + new string('c', 64), "amd64");
        _docker.Inspector = (_, _) => Task.FromResult(DockerDouble.Inspected("exited", 0));
    }

    public void Dispose()
    {
        Directory.Delete(_bundles, recursive: true);
        GC.SuppressFinalize(this);
    }

    private WorkspaceRuntimeOptions Options(Func<WorkspaceRuntimeOptions, WorkspaceRuntimeOptions>? change = null)
    {
        var options = new WorkspaceRuntimeOptions("test-installation", new FakeVendor().Lock, AgentArchitecture.X64)
        {
            ToolsImage = ToolsImage,
            BundlesDirectory = _bundles,
            CheckoutPollInterval = TimeSpan.FromMilliseconds(5),
            CheckoutLimit = TimeSpan.FromSeconds(30),
        };
        return change is null ? options : change(options);
    }

    private BundleReference WriteBundle(string name = "task/step-checkout.bundle", byte[]? content = null)
    {
        var path = Path.Combine(_bundles, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content ?? _content);
        return new BundleReference(name, Convert.ToHexStringLower(SHA256.HashData(content ?? _content)), new string('d', 40));
    }

    private Task CheckoutAsync(BundleReference bundle, WorkspaceRuntimeOptions? options = null) =>
        new DockerWorkspaceRuntime(_docker.Client, options ?? Options(), new RecordingWatcher())
            .CheckoutAsync(new CheckoutRequest(TaskId, StepRunId, Branch, bundle), Key, TestContext.Current.CancellationToken);

    private async Task<CreateContainerParameters> CheckedOutAsync()
    {
        await CheckoutAsync(WriteBundle());
        return Assert.Single(_docker.Created);
    }

    [Fact]
    public async Task The_checkout_container_is_the_tools_image_as_the_step_user_with_no_network_and_every_capability_dropped()
    {
        var container = await CheckedOutAsync();

        Assert.Equal($"hephaestus-checkout-{StepRunId}", container.Name);
        Assert.Equal(ToolsImage, container.Image);
        Assert.Equal("10001:10001", container.User);
        Assert.Equal("none", container.HostConfig!.NetworkMode);
        Assert.Equal(["ALL"], container.HostConfig.CapDrop);
        Assert.Equal(["no-new-privileges"], container.HostConfig.SecurityOpt);
        Assert.False(container.HostConfig.Privileged);
        Assert.False(container.HostConfig.PublishAllPorts);
        Assert.Equal(512, container.HostConfig.PidsLimit);
        Assert.Equal(1024L * 1024 * 1024, container.HostConfig.Memory);
        Assert.Equal(container.HostConfig!.Memory, container.HostConfig.MemorySwap);
        Assert.Equal(1_000_000_000L, container.HostConfig.NanoCPUs);
    }

    [Fact]
    public async Task The_checkout_container_carries_the_labels_of_the_convention_and_its_role()
    {
        var container = await CheckedOutAsync();

        Assert.Equal("test-installation", container.Labels!["hephaestus.installation"]);
        Assert.Equal(TaskId.ToString(), container.Labels!["hephaestus.task"]);
        Assert.Equal(StepRunId.ToString(), container.Labels!["hephaestus.step-run"]);
        Assert.Equal("checkout", container.Labels!["hephaestus.role"]);
        Assert.Equal(Key, container.Labels!["hephaestus.effect"]);
    }

    [Fact]
    public async Task The_only_mount_is_the_task_volume_without_copying_from_the_image_and_no_host_path_is_bound()
    {
        var container = await CheckedOutAsync();

        var mount = Assert.Single(container.HostConfig!.Mounts!);
        Assert.Equal("volume", mount.Type);
        Assert.Equal($"hephaestus-ws-{TaskId}", mount.Source);
        Assert.Equal("/workspace", mount.Target);
        Assert.False(mount.ReadOnly);
        Assert.True(mount.VolumeOptions!.NoCopy);
        Assert.True(container.HostConfig.Binds is null or { Count: 0 });
        Assert.Empty(_docker.Volumes);
        Assert.Empty(_docker.Networks);
    }

    [Fact]
    public async Task The_environment_holds_the_branch_the_tip_and_a_home_and_no_credential()
    {
        var container = await CheckedOutAsync();

        Assert.Equal([$"HEPHAESTUS_BRANCH={Branch}", $"HEPHAESTUS_TIP={new string('d', 40)}", "HOME=/tmp"], container.Env);
        Assert.All(container.Env!, variable => Assert.DoesNotMatch("(?i)token|secret|key|password|credential|github", variable.Split('=')[0]));
        Assert.Equal(["sh", "-c", CheckoutScript.Text, "checkout", "/workspace", "/checkout/task.bundle"], container.Entrypoint);
        Assert.Empty(container.Cmd!);
    }

    [Fact]
    public async Task The_bundle_is_the_one_file_the_container_is_given_and_is_owned_by_the_step_user()
    {
        await CheckedOutAsync();

        var extraction = Assert.Single(_docker.Extractions);
        Assert.Equal("/", extraction.Parameters.Path);
        Assert.True(extraction.Parameters.CopyUIDGID);
        using var reader = new TarReader(new MemoryStream(extraction.Tar));
        var directory = reader.GetNextEntry()!;
        Assert.Equal(TarEntryType.Directory, directory.EntryType);
        Assert.Equal("checkout", directory.Name.TrimEnd('/'));
        Assert.Equal(10001, directory.Uid);
        var file = reader.GetNextEntry()!;
        Assert.Equal("checkout/task.bundle", file.Name);
        Assert.Equal(10001, file.Uid);
        Assert.Equal(10001, file.Gid);
        Assert.Equal(UnixFileMode.UserRead, file.Mode);
        using var content = new MemoryStream();
        file.DataStream!.CopyTo(content);
        Assert.Equal(_content, content.ToArray());
        Assert.Null(reader.GetNextEntry());
    }

    [Fact]
    public async Task The_container_is_started_once_and_removed_when_it_ends_and_so_is_the_bundle()
    {
        var bundle = WriteBundle();

        await CheckoutAsync(bundle);

        Assert.Equal([$"id-hephaestus-checkout-{StepRunId}"], _docker.Started);
        Assert.Contains($"id-hephaestus-checkout-{StepRunId}", _docker.Removed);
        Assert.True(_docker.Removals.Last().Force);
        Assert.False(File.Exists(Path.Combine(_bundles, "task", "step-checkout.bundle")));
        Assert.Empty(Directory.EnumerateFiles(_bundles, ".checkout-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_container_a_crash_left_for_the_step_run_is_replaced_by_name()
    {
        await CheckedOutAsync();

        Assert.Equal($"remove hephaestus-checkout-{StepRunId}", _docker.Calls.First(call => call.StartsWith("remove ", StringComparison.Ordinal)));
        Assert.True(_docker.Calls.IndexOf($"remove hephaestus-checkout-{StepRunId}") < _docker.Calls.IndexOf($"create container hephaestus-checkout-{StepRunId}"));
    }

    [Fact]
    public async Task A_checkout_that_ends_with_a_failure_names_the_exit_code_and_still_removes_the_container_and_the_bundle()
    {
        _docker.Inspector = (_, _) => Task.FromResult(DockerDouble.Inspected("exited", 128));
        var bundle = WriteBundle();

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckoutAsync(bundle));

        Assert.Equal(MessageIds.WorkspaceCheckoutFailed, failure.Info.Id);
        Assert.Equal("128", failure.Info.Arguments[0]);
        Assert.Contains($"id-hephaestus-checkout-{StepRunId}", _docker.Removed);
        Assert.False(File.Exists(Path.Combine(_bundles, "task", "step-checkout.bundle")));
    }

    [Fact]
    public async Task A_checkout_that_does_not_end_in_time_is_removed_by_force_and_never_stopped_by_the_runtime()
    {
        _docker.Inspector = (_, _) => Task.FromResult(DockerDouble.Inspected("running"));
        var bundle = WriteBundle();

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckoutAsync(bundle, Options(o => o with { CheckoutLimit = TimeSpan.FromMilliseconds(40) })));

        Assert.Equal(MessageIds.WorkspaceCheckoutTimedOut, failure.Info.Id);
        Assert.Empty(_docker.Stops);
        Assert.Contains($"id-hephaestus-checkout-{StepRunId}", _docker.Removed);
        Assert.True(_docker.Removals.Last().Force);
    }

    [Fact]
    public async Task A_bundle_whose_checksum_is_not_the_one_written_is_refused_before_the_container_gets_it_and_is_deleted()
    {
        var bundle = WriteBundle();
        File.WriteAllBytes(Path.Combine(_bundles, "task", "step-checkout.bundle"), [1, 2, 3]);

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckoutAsync(bundle));

        Assert.Equal(MessageIds.WorkspaceCheckoutBundleChecksum, failure.Info.Id);
        Assert.Empty(_docker.Extractions);
        Assert.Empty(_docker.Started);
        Assert.Contains($"id-hephaestus-checkout-{StepRunId}", _docker.Removed);
        Assert.False(File.Exists(Path.Combine(_bundles, "task", "step-checkout.bundle")));
    }

    [Fact]
    public async Task A_bundle_that_does_not_exist_is_refused_before_anything_is_asked_of_docker()
    {
        var bundle = new BundleReference("task/missing.bundle", new string('0', 64), new string('d', 40));

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckoutAsync(bundle));

        Assert.Equal(MessageIds.WorkspaceCheckoutBundleFile, failure.Info.Id);
        Assert.Empty(_docker.Calls);
    }

    [Fact]
    public async Task A_bundle_larger_than_the_bound_is_refused_and_deleted()
    {
        var bundle = WriteBundle();

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckoutAsync(bundle, Options(o => o with { MaximumBundleBytes = 4 })));

        Assert.Equal(MessageIds.WorkspaceCheckoutBundleSize, failure.Info.Id);
        Assert.Equal<object[]>([bundle.Path, "4"], failure.Info.Arguments);
        Assert.Empty(_docker.Calls);
        Assert.False(File.Exists(Path.Combine(_bundles, "task", "step-checkout.bundle")));
    }

    [Fact]
    public async Task A_link_in_place_of_the_bundle_is_refused_even_when_the_file_it_points_to_has_the_right_checksum_and_only_the_link_is_deleted()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.bundle");
        await File.WriteAllBytesAsync(outside, _content, TestContext.Current.CancellationToken);
        try
        {
            Directory.CreateDirectory(Path.Combine(_bundles, "task"));
            File.CreateSymbolicLink(Path.Combine(_bundles, "task", "linked.bundle"), outside);
            var bundle = new BundleReference("task/linked.bundle", Convert.ToHexStringLower(SHA256.HashData(_content)), new string('d', 40));

            var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckoutAsync(bundle));

            Assert.Equal(MessageIds.WorkspaceCheckoutBundleFile, failure.Info.Id);
            Assert.Empty(_docker.Calls);
            Assert.False(File.Exists(Path.Combine(_bundles, "task", "linked.bundle")) || Path.Exists(Path.Combine(_bundles, "task", "linked.bundle")));
            Assert.True(File.Exists(outside));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task A_bundles_directory_named_with_a_trailing_separator_works_as_well()
    {
        var bundle = WriteBundle();

        await CheckoutAsync(bundle, Options(o => o with { BundlesDirectory = _bundles + Path.DirectorySeparatorChar }));

        Assert.Single(_docker.Started);
    }

    [Theory]
    [InlineData(CheckoutScript.BundleNotVerified, MessageIds.WorkspaceCheckoutBundleNotVerified)]
    [InlineData(CheckoutScript.BundleHeads, MessageIds.WorkspaceCheckoutBundleHeads)]
    [InlineData(CheckoutScript.BundleTip, MessageIds.WorkspaceCheckoutBundleTip)]
    public async Task A_checkout_that_refuses_the_bundle_says_which_way_in_a_message_of_its_own(int exitCode, string expected)
    {
        _docker.Inspector = (_, _) => Task.FromResult(DockerDouble.Inspected("exited", exitCode));
        var bundle = WriteBundle();

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckoutAsync(bundle));

        Assert.Equal(expected, failure.Info.Id);
        Assert.Equal<object[]>([bundle.Path], failure.Info.Arguments);
    }

    [Fact]
    public async Task Any_other_failure_names_the_exit_code_and_the_last_of_what_the_container_printed()
    {
        _docker.Inspector = (_, _) => Task.FromResult(DockerDouble.Inspected("exited", 128));
        _docker.Logs = (_, _) => Task.FromResult(DockerDouble.Output((2, new string('x', 600) + "fatal: bad object\n")));
        var bundle = WriteBundle();

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckoutAsync(bundle));

        Assert.Equal(MessageIds.WorkspaceCheckoutFailed, failure.Info.Id);
        Assert.Equal("128", failure.Info.Arguments[0]);
        var printed = (string)failure.Info.Arguments[1];
        Assert.EndsWith("fatal: bad object", printed, StringComparison.Ordinal);
        Assert.Equal(500, printed.Length);
    }

    [Fact]
    public async Task A_container_that_was_created_and_has_not_started_is_waited_for_and_not_taken_for_a_finished_checkout()
    {
        var asked = 0;
        _docker.Inspector = (_, _) => Task.FromResult(++asked < 3 ? DockerDouble.Inspected("created") : DockerDouble.Inspected("exited", 0));

        await CheckoutAsync(WriteBundle());

        Assert.True(asked >= 3);
    }

    [Fact]
    public async Task A_container_that_is_dead_has_made_nothing_whatever_its_exit_code_says()
    {
        _docker.Inspector = (_, _) => Task.FromResult(DockerDouble.Inspected("dead", 0));
        var bundle = WriteBundle();

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckoutAsync(bundle));

        Assert.Equal(MessageIds.WorkspaceCheckoutFailed, failure.Info.Id);
        Assert.Equal("-1", failure.Info.Arguments[0]);
    }

    [Fact]
    public async Task A_task_whose_volume_does_not_exist_gets_no_checkout_and_no_volume_is_made_for_it()
    {
        _docker.TaskVolumeExists = false;
        var bundle = WriteBundle();

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckoutAsync(bundle));

        Assert.Equal(MessageIds.WorkspaceTaskVolumeMissing, failure.Info.Id);
        Assert.Equal<object[]>([$"hephaestus-ws-{TaskId}"], failure.Info.Arguments);
        Assert.Empty(_docker.Created);
        Assert.Empty(_docker.Volumes);
        Assert.False(File.Exists(Path.Combine(_bundles, "task", "step-checkout.bundle")));
    }

    [Fact]
    public async Task The_tools_image_that_is_on_the_machine_is_not_fetched()
    {
        await CheckedOutAsync();

        Assert.Empty(_docker.Pulls);
    }

    [Fact]
    public async Task A_machine_without_the_tools_image_fetches_it_for_its_architecture_and_then_makes_the_checkout()
    {
        _docker.Image = null;
        _docker.PulledImage = DockerDouble.ImageOf("sha256:" + new string('c', 64), "amd64");

        await CheckedOutAsync();

        var pull = Assert.Single(_docker.Pulls);
        Assert.Equal(ToolsImage, pull.FromImage);
        Assert.Equal("linux/amd64", pull.Platform);
    }

    [Fact]
    public async Task A_tools_image_that_cannot_be_fetched_stops_the_checkout_and_names_the_image()
    {
        _docker.Image = null;
        _docker.PullFails = new HttpRequestException("registry unreachable");
        var bundle = WriteBundle();

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckoutAsync(bundle));

        Assert.Equal(MessageIds.WorkspaceToolsImageUnavailable, failure.Info.Id);
        Assert.Equal(ToolsImage, failure.Info.Arguments[0]);
        Assert.Empty(_docker.Created);
        Assert.False(File.Exists(Path.Combine(_bundles, "task", "step-checkout.bundle")));
    }

    [Fact]
    public async Task A_pull_that_reports_an_error_in_its_stream_stops_the_checkout()
    {
        _docker.Image = null;
        _docker.PullMessages.Add(new JSONMessage { Error = new JSONError { Message = "manifest unknown" } });
        var bundle = WriteBundle();

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckoutAsync(bundle));

        Assert.Equal(MessageIds.WorkspaceToolsImageUnavailable, failure.Info.Id);
        Assert.Contains("manifest unknown", (string)failure.Info.Arguments[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pull_that_leaves_no_image_on_the_machine_stops_the_checkout()
    {
        _docker.Image = null;
        var bundle = WriteBundle();

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckoutAsync(bundle));

        Assert.Equal(MessageIds.WorkspaceToolsImageMissing, failure.Info.Id);
    }

    [Fact]
    public async Task A_container_that_cannot_be_created_because_the_image_is_gone_names_the_image()
    {
        _docker.FailCreate = _ => DockerDouble.NotFound();
        var bundle = WriteBundle();

        var failure = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => CheckoutAsync(bundle));

        Assert.Equal(MessageIds.WorkspaceToolsImageUnavailable, failure.Info.Id);
    }

    [Fact]
    public async Task The_runtime_makes_no_checkout_for_a_request_it_cannot_read()
    {
        var runtime = new DockerWorkspaceRuntime(_docker.Client, Options(), new RecordingWatcher());

        await Assert.ThrowsAsync<ArgumentNullException>(() => runtime.CheckoutAsync(null!, Key, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_options_name_the_tools_image_the_runtime_is_given()
    {
        var options = Options();

        Assert.Equal(ToolsImage, options.ToolsImage);
        Assert.Equal("ghcr.io/adamcharusta/hephaestus-tools:latest", new WorkspaceRuntimeOptions("x", new FakeVendor().Lock, AgentArchitecture.X64).ToolsImage);
    }
}
