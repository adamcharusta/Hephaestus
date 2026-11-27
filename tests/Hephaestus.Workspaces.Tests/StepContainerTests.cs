using System.Formats.Tar;
using Docker.DotNet;
using Docker.DotNet.Models;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Workspaces;
using NSubstitute;

namespace Hephaestus.Workspaces.Tests;

/// <summary>Story 3.2, AD-7, AD-18, AD-5: what the runtime asks Docker for when it starts a step, checked against an Engine in memory.</summary>
public class StepContainerTests
{
    private static readonly Guid TaskId = Guid.Parse("018f4b6e-0000-7000-8000-000000000001");
    private static readonly Guid StepRunId = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");
    private const string Installation = "test-installation";
    private const string Key = "task:s1:container:0";
    private static readonly DateTimeOffset Deadline = new(2026, 11, 27, 13, 0, 0, TimeSpan.Zero);

    private readonly DockerDouble _docker = new();
    private readonly FakeVendor _vendor = new();
    private readonly RecordingWatcher _watcher = new();

    private DockerWorkspaceRuntime Runtime(AgentArchitecture architecture = AgentArchitecture.X64) =>
        new(_docker.Client, new WorkspaceRuntimeOptions(Installation, _vendor.Lock, architecture), _watcher);

    private static ResolvedImage Image(CLibrary libc = CLibrary.Glibc, AgentArchitecture architecture = AgentArchitecture.X64) =>
        new("registry.test/app@sha256:" + new string('a', 64), "sha256:" + new string('a', 64), architecture, libc);

    private static StepContainerRequest Request(
        ResolvedImage? image = null,
        StepLimits? limits = null,
        IReadOnlyList<string>? command = null,
        IReadOnlyDictionary<string, string>? environment = null,
        IReadOnlyList<WorkspaceFile>? context = null,
        IReadOnlyList<WorkspaceFile>? methodology = null,
        DateTimeOffset? deadline = null) =>
        new(
            TaskId,
            StepRunId,
            image ?? Image(),
            command ?? ["/hephaestus/agents/claude-code/claude", "-p", "Read /hephaestus/context/instruction.md"],
            environment ?? new Dictionary<string, string> { ["ANTHROPIC_MODEL"] = "model" },
            limits ?? new StepLimits(2500, 3L * 1024 * 1024 * 1024),
            context ?? [new WorkspaceFile("instruction.md", [1])],
            methodology ?? [new WorkspaceFile("manifest.yml", [2])],
            deadline ?? Deadline);

    private async Task<CreateContainerParameters> StartedAsync(StepContainerRequest? request = null)
    {
        await Runtime().StartStepAsync(request ?? Request(), Key, TestContext.Current.CancellationToken);
        return _docker.Created.Single(created => created.Name == $"hephaestus-step-{StepRunId}");
    }

    [Fact]
    public async Task The_step_container_runs_as_the_step_user_with_every_capability_dropped_and_no_new_privileges()
    {
        var container = await StartedAsync();
        var host = container.HostConfig!;

        Assert.Equal("10001:10001", container.User);
        Assert.Equal(["ALL"], host.CapDrop!);
        Assert.Empty(host.CapAdd ?? []);
        Assert.Equal(["no-new-privileges"], host.SecurityOpt!);
        Assert.False(host.Privileged);
    }

    [Fact]
    public async Task The_default_seccomp_profile_applies_because_none_is_named_and_nothing_weakens_it()
    {
        var host = (await StartedAsync()).HostConfig!;

        Assert.DoesNotContain(host.SecurityOpt!, option => option.StartsWith("seccomp", StringComparison.Ordinal));
        Assert.DoesNotContain(host.SecurityOpt!, option => option.StartsWith("apparmor", StringComparison.Ordinal));
        Assert.True(string.IsNullOrEmpty(host.PidMode));
        Assert.True(string.IsNullOrEmpty(host.IpcMode));
        Assert.True(string.IsNullOrEmpty(host.UsernsMode));
        Assert.Empty(host.Devices! ?? []);
    }

    [Fact]
    public async Task The_container_has_a_process_limit_and_no_published_port()
    {
        var container = await StartedAsync();

        Assert.Equal(512, container.HostConfig!.PidsLimit);
        Assert.False(container.HostConfig!.PublishAllPorts);
        Assert.Empty(container.HostConfig!.PortBindings! ?? new Dictionary<string, IList<PortBinding>>());
        Assert.Empty(container.ExposedPorts! ?? new Dictionary<string, EmptyStruct>());
    }

    [Fact]
    public async Task Processor_and_memory_come_from_the_limits_of_the_request_and_swap_adds_no_memory()
    {
        var host = (await StartedAsync(Request(limits: new StepLimits(2500, 3L * 1024 * 1024 * 1024)))).HostConfig!;

        Assert.Equal(2_500_000_000, host.NanoCPUs);
        Assert.Equal(3L * 1024 * 1024 * 1024, host.Memory);
        Assert.Equal(host.Memory, host.MemorySwap);
    }

    [Fact]
    public async Task The_container_is_attached_to_the_workspace_network_alone_and_is_not_restarted_by_docker()
    {
        var host = (await StartedAsync()).HostConfig!;

        Assert.Equal("hephaestus-workspace", host.NetworkMode);
        Assert.Equal(RestartPolicyKind.No, host.RestartPolicy!.Name);
        Assert.Empty(host.Binds! ?? []);
        Assert.Empty(host.VolumesFrom! ?? []);
        Assert.Empty(host.Links! ?? []);
    }

    [Fact]
    public async Task The_mounts_are_exactly_the_task_volume_the_context_the_methodology_and_the_agent_binaries_and_a_temporary_home()
    {
        var host = (await StartedAsync()).HostConfig!;

        Assert.Equal(
            [
                "/hephaestus/agents/certs",
                "/hephaestus/agents/claude-code",
                "/hephaestus/agents/codex-cli",
                "/hephaestus/context",
                "/hephaestus/methodology",
                "/workspace",
            ],
            host.Mounts!.Select(mount => mount.Target!).Order(StringComparer.Ordinal));
        Assert.All(host.Mounts!, mount => Assert.Equal("volume", mount.Type));
        Assert.Equal(["/hephaestus/home"], host.Tmpfs!.Keys);
    }

    [Fact]
    public async Task Only_the_task_volume_is_writable_and_every_named_volume_is_mounted_without_copying_from_the_image()
    {
        var mounts = (await StartedAsync()).HostConfig!.Mounts!;

        Assert.Equal(["/workspace"], mounts.Where(mount => mount.ReadOnly != true).Select(mount => mount.Target!));
        Assert.All(mounts, mount => Assert.True(mount.VolumeOptions!.NoCopy));
    }

    [Fact]
    public async Task The_sources_are_the_volumes_of_this_task_and_this_step_run_and_the_agent_binaries_volume()
    {
        var mounts = (await StartedAsync()).HostConfig!.Mounts!.ToDictionary(mount => mount.Target!, mount => mount.Source!);

        Assert.Equal($"hephaestus-ws-{TaskId}", mounts["/workspace"]);
        Assert.Equal($"hephaestus-context-{StepRunId}", mounts["/hephaestus/context"]);
        Assert.Equal($"hephaestus-methodology-{StepRunId}", mounts["/hephaestus/methodology"]);
        Assert.Equal("hephaestus-agents", mounts["/hephaestus/agents/claude-code"]);
        Assert.Equal("hephaestus-agents", mounts["/hephaestus/agents/codex-cli"]);
        Assert.Equal("hephaestus-agents", mounts["/hephaestus/agents/certs"]);
    }

    [Fact]
    public async Task The_home_is_a_tmpfs_owned_by_the_step_user_that_only_it_can_enter()
    {
        var container = await StartedAsync();
        var options = container.HostConfig!.Tmpfs!["/hephaestus/home"].Split(',');

        Assert.Contains("uid=10001", options);
        Assert.Contains("gid=10001", options);
        Assert.Contains("mode=0700", options);
        Assert.Contains("nosuid", options);
        Assert.Contains("nodev", options);
        Assert.Contains("HOME=/hephaestus/home", container.Env!);
    }

    [Theory]
    [InlineData(CLibrary.Glibc, "claude-code/x64-glibc")]
    [InlineData(CLibrary.Musl, "claude-code/x64-musl")]
    public async Task The_build_of_Claude_Code_that_matches_the_images_C_library_is_mounted_at_the_same_path(CLibrary libc, string subpath)
    {
        var mounts = (await StartedAsync(Request(image: Image(libc)))).HostConfig!.Mounts!.ToDictionary(mount => mount.Target!);

        Assert.Equal(subpath, mounts["/hephaestus/agents/claude-code"].VolumeOptions!.Subpath);
    }

    [Theory]
    [InlineData(CLibrary.Glibc)]
    [InlineData(CLibrary.Musl)]
    public async Task Codex_CLI_and_the_certificates_are_the_same_in_every_image(CLibrary libc)
    {
        var mounts = (await StartedAsync(Request(image: Image(libc)))).HostConfig!.Mounts!.ToDictionary(mount => mount.Target!);

        Assert.Equal("codex-cli/x64-static", mounts["/hephaestus/agents/codex-cli"].VolumeOptions!.Subpath);
        Assert.Equal("certs", mounts["/hephaestus/agents/certs"].VolumeOptions!.Subpath);
    }

    [Fact]
    public async Task On_arm64_the_builds_for_arm64_are_mounted()
    {
        await Runtime(AgentArchitecture.Arm64).StartStepAsync(Request(image: Image(CLibrary.Musl, AgentArchitecture.Arm64)), Key, TestContext.Current.CancellationToken);

        var mounts = _docker.Created.Single(created => created.Name!.StartsWith("hephaestus-step-", StringComparison.Ordinal)).HostConfig!.Mounts!.ToDictionary(mount => mount.Target!);
        Assert.Equal("claude-code/arm64-musl", mounts["/hephaestus/agents/claude-code"].VolumeOptions!.Subpath);
        Assert.Equal("codex-cli/arm64-static", mounts["/hephaestus/agents/codex-cli"].VolumeOptions!.Subpath);
    }

    [Fact]
    public async Task The_container_carries_the_labels_of_the_convention_and_a_name_that_starts_with_hephaestus()
    {
        var container = await StartedAsync();

        Assert.StartsWith("hephaestus-", container.Name, StringComparison.Ordinal);
        Assert.Equal(Installation, container.Labels!["hephaestus.installation"]);
        Assert.Equal(TaskId.ToString(), container.Labels!["hephaestus.task"]);
        Assert.Equal(StepRunId.ToString(), container.Labels!["hephaestus.step-run"]);
        Assert.Equal(Key, container.Labels!["hephaestus.effect"]);
    }

    [Fact]
    public async Task The_image_cannot_change_what_runs_because_the_entry_point_and_the_working_directory_are_replaced()
    {
        var container = await StartedAsync();

        Assert.Equal(["/hephaestus/agents/claude-code/claude"], container.Entrypoint!);
        Assert.Equal(["-p", "Read /hephaestus/context/instruction.md"], container.Cmd!);
        Assert.Equal("/workspace", container.WorkingDir);
        Assert.Equal("registry.test/app@sha256:" + new string('a', 64), container.Image);
    }

    [Fact]
    public async Task The_environment_of_the_invocation_is_set_and_HOME_is_always_the_temporary_one()
    {
        var container = await StartedAsync(Request(environment: new Dictionary<string, string> { ["HOME"] = "/root", ["TOKEN_FILE"] = "/x" }));

        Assert.Equal(["TOKEN_FILE=/x", "HOME=/hephaestus/home"], container.Env!);
    }

    [Theory]
    [InlineData("A=B")]
    [InlineData("")]
    [InlineData("A\0B")]
    public async Task An_environment_name_that_cannot_be_one_is_refused_before_anything_is_created(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Runtime().StartStepAsync(Request(environment: new Dictionary<string, string> { [name] = "v" }), Key, TestContext.Current.CancellationToken));

        Assert.Empty(_docker.Calls);
    }

    [Fact]
    public async Task A_value_with_a_null_character_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Runtime().StartStepAsync(Request(environment: new Dictionary<string, string> { ["A"] = "x\0y" }), Key, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0, 1024L * 1024 * 1024)]
    [InlineData(-1, 1024L * 1024 * 1024)]
    [InlineData(1000, 0)]
    [InlineData(1000, 1024 * 1024)]
    public async Task A_limit_that_docker_would_read_as_no_limit_or_refuse_is_refused_before_anything_is_created(int cpuMillis, long memoryBytes)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Runtime().StartStepAsync(Request(limits: new StepLimits(cpuMillis, memoryBytes)), Key, TestContext.Current.CancellationToken));

        Assert.Empty(_docker.Calls);
    }

    [Fact]
    public async Task Files_that_cannot_be_written_by_one_archive_are_refused_before_anything_is_created()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Runtime().StartStepAsync(Request(context: [new WorkspaceFile("a", [1]), new WorkspaceFile("a", [2])]), Key, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => Runtime().StartStepAsync(Request(methodology: [new WorkspaceFile("steps", [1]), new WorkspaceFile("steps/review.md", [2])]), Key, TestContext.Current.CancellationToken));

        Assert.Empty(_docker.Calls);
    }

    [Fact]
    public async Task A_request_without_a_command_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Runtime().StartStepAsync(Request(command: []), Key, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => Runtime().StartStepAsync(Request(command: [""]), Key, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Runtime().StartStepAsync(null!, Key, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_call_returns_once_the_container_is_started_and_does_not_wait_for_the_step_to_end()
    {
        var started = await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        Assert.True(started.Created);
        Assert.Equal($"hephaestus-step-{StepRunId}", started.Name);
        Assert.Equal($"id-hephaestus-step-{StepRunId}", started.ContainerId);
        Assert.Equal([started.ContainerId], _docker.Started);
        await _docker.Client.Containers.DidNotReceiveWithAnyArgs().WaitContainerAsync(default!, TestContext.Current.CancellationToken);
        await _docker.Client.Containers.DidNotReceiveWithAnyArgs().AttachContainerAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_container_is_started_after_it_is_created_and_after_its_files_are_in_place()
    {
        await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        var calls = _docker.Calls;
        Assert.True(calls.IndexOf($"create container hephaestus-step-{StepRunId}") > calls.IndexOf($"extract id-hephaestus-file-fill-{StepRunId} /context"));
        Assert.Equal($"start id-hephaestus-step-{StepRunId}", calls[^1]);
    }

    [Fact]
    public async Task A_container_of_the_step_run_that_exists_is_returned_and_no_second_one_is_made()
    {
        _docker.Existing.Add(new ContainerListResponse { ID = "found", Names = ["/hephaestus-step-x"], State = "running" });

        var started = await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        Assert.False(started.Created);
        Assert.Equal("found", started.ContainerId);
        Assert.Equal("hephaestus-step-x", started.Name);
        Assert.Empty(_docker.Created);
        Assert.Empty(_docker.Started);
        Assert.Empty(_docker.Volumes);
    }

    [Fact]
    public async Task The_step_runs_container_is_found_by_its_labels_and_not_by_its_name()
    {
        _docker.Existing.Add(new ContainerListResponse { ID = "found", State = "exited" });

        await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        var filter = Assert.Single(_docker.Listings).Filters!["label"].Keys;
        Assert.Equal(
            [$"hephaestus.installation={Installation}", "hephaestus.role=step", $"hephaestus.step-run={StepRunId}"],
            filter.Order(StringComparer.Ordinal));
        Assert.True(_docker.Listings[0].All);
    }

    [Fact]
    public async Task A_container_that_was_created_and_never_started_is_started_when_a_restart_finds_it()
    {
        _docker.Existing.Add(new ContainerListResponse { ID = "found", Names = ["/hephaestus-step-x"], State = "created" });

        var started = await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        Assert.False(started.Created);
        Assert.Equal(["found"], _docker.Started);
        Assert.Empty(_docker.Created);
    }

    [Fact]
    public async Task A_container_with_no_name_is_still_returned()
    {
        _docker.Existing.Add(new ContainerListResponse { ID = "found", State = "exited" });

        var started = await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, started.Name);
    }

    [Fact]
    public async Task Two_calls_for_one_step_run_that_meet_in_docker_end_with_one_container()
    {
        _docker.FailCreate = parameters =>
        {
            if (parameters.Labels!["hephaestus.role"] != "step")
            {
                return null;
            }

            _docker.Existing.Add(new ContainerListResponse { ID = "winner", Names = ["/" + parameters.Name], State = "created" });
            return DockerDouble.Conflict();
        };

        var started = await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        Assert.False(started.Created);
        Assert.Equal("winner", started.ContainerId);
        Assert.Equal(["winner"], _docker.Started);
        Assert.Equal("winner", Assert.Single(_watcher.Watched).ContainerId);
    }

    [Fact]
    public async Task A_name_that_is_taken_by_something_that_is_not_the_step_runs_container_is_a_failure()
    {
        _docker.FailCreate = parameters => parameters.Labels!["hephaestus.role"] == "step" ? DockerDouble.Conflict() : null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Another_refusal_of_the_engine_is_not_hidden()
    {
        _docker.FailCreate = parameters => parameters.Labels!["hephaestus.role"] == "step" ? new DockerApiException(System.Net.HttpStatusCode.InternalServerError, "boom") : null;

        var failure = await Assert.ThrowsAsync<DockerApiException>(() => Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken));

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, failure.StatusCode);
        Assert.Empty(_docker.Started);
    }

    [Fact]
    public async Task The_workspace_network_is_created_internal_when_it_is_missing()
    {
        await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        var network = Assert.Single(_docker.Networks);
        Assert.Equal("hephaestus-workspace", network.Name);
        Assert.True(network.Internal);
        Assert.StartsWith("hephaestus-", network.Name, StringComparison.Ordinal);
        Assert.Equal(Installation, network.Labels!["hephaestus.installation"]);
        Assert.Equal(Key, network.Labels!["hephaestus.effect"]);
    }

    [Fact]
    public async Task A_workspace_network_that_has_a_route_out_is_never_used()
    {
        _docker.NetworkExists = true;
        _docker.NetworkInternal = false;

        var refusal = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken));

        Assert.Equal(Hephaestus.Core.Localization.MessageIds.WorkspaceNetworkNotInternal, refusal.Info.Id);
        Assert.Equal(["hephaestus-workspace"], refusal.Info.Arguments);
        Assert.Empty(_docker.Created);
        Assert.Empty(_docker.Started);
    }

    [Fact]
    public async Task A_network_that_something_else_made_in_between_is_checked_too()
    {
        _docker.NetworkCreateFails = DockerDouble.Conflict();
        _docker.NetworkInternal = false;
        var inspections = 0;
        _docker.Client.Networks.InspectNetworkAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
            ++inspections == 1
                ? Task.FromException<NetworkResponse>(DockerDouble.NotFound())
                : Task.FromResult(new NetworkResponse { Name = "hephaestus-workspace", Internal = false }));

        await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken));

        Assert.Empty(_docker.Started);
    }

    [Fact]
    public async Task A_container_that_cannot_start_is_removed_so_that_a_retry_makes_a_new_one()
    {
        _docker.StartFails = new DockerApiException(System.Net.HttpStatusCode.BadRequest, "executable file not found");

        await Assert.ThrowsAsync<DockerApiException>(() => Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken));

        Assert.Contains($"id-hephaestus-step-{StepRunId}", _docker.Removed);
    }

    [Fact]
    public async Task A_helper_that_cannot_be_removed_because_the_engine_is_gone_does_not_hide_how_the_work_went()
    {
        _docker.RemoveFails = container => container.StartsWith("id-", StringComparison.Ordinal) ? new HttpRequestException("connection refused") : null;

        var started = await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        Assert.True(started.Created);
    }

    [Fact]
    public async Task Helpers_are_removed_with_the_anonymous_volumes_an_image_declares_and_their_entry_point_is_named()
    {
        await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        var helper = _docker.Created.Single(created => created.Labels!["hephaestus.role"] == "file-fill");
        Assert.Equal(["/hephaestus-never-started"], helper.Entrypoint);
        Assert.All(_docker.Removals, removal =>
        {
            Assert.True(removal.Force);
            Assert.True(removal.RemoveVolumes);
        });
    }

    [Fact]
    public async Task The_workspace_network_is_left_alone_when_it_exists()
    {
        _docker.NetworkExists = true;

        await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        Assert.Empty(_docker.Networks);
    }

    [Fact]
    public async Task A_network_that_another_call_made_in_between_is_not_a_failure()
    {
        _docker.NetworkCreateFails = DockerDouble.Conflict();
        var inspections = 0;
        _docker.Client.Networks.InspectNetworkAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
            ++inspections == 1
                ? Task.FromException<NetworkResponse>(DockerDouble.NotFound())
                : Task.FromResult(new NetworkResponse { Name = "hephaestus-workspace", Internal = true }));

        var started = await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        Assert.True(started.Created);
    }

    [Fact]
    public async Task The_context_and_the_methodology_are_placed_in_volumes_of_the_step_run_before_the_container_mounts_them()
    {
        await Runtime().StartStepAsync(Request(context: [new WorkspaceFile("instruction.md", [1]), new WorkspaceFile("task.json", [2])], methodology: [new WorkspaceFile("steps/review.md", [3])]), Key, TestContext.Current.CancellationToken);

        Assert.Equal([$"hephaestus-context-{StepRunId}", $"hephaestus-methodology-{StepRunId}"], _docker.Volumes.Select(volume => volume.Name));
        Assert.All(_docker.Volumes, volume =>
        {
            Assert.Equal(StepRunId.ToString(), volume.Labels!["hephaestus.step-run"]);
            Assert.Equal(TaskId.ToString(), volume.Labels!["hephaestus.task"]);
            Assert.Equal(Installation, volume.Labels!["hephaestus.installation"]);
        });
        Assert.Equal(["/context", "/methodology"], _docker.Extractions.Select(extraction => extraction.Parameters.Path));
        Assert.All(_docker.Extractions, extraction => Assert.True(extraction.Parameters.CopyUIDGID));
        Assert.Equal(["instruction.md", "task.json"], EntryNames(_docker.Extractions[0].Tar));
        Assert.Equal(["steps", "steps/review.md"], EntryNames(_docker.Extractions[1].Tar));
    }

    [Fact]
    public async Task The_files_are_readable_by_every_user_and_writable_by_none()
    {
        await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        using var reader = new TarReader(new MemoryStream(_docker.Extractions[0].Tar));
        var entry = reader.GetNextEntry()!;
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead, entry.Mode);
        Assert.Equal(0, entry.Uid);
        Assert.Equal(0, entry.Gid);
    }

    [Fact]
    public async Task The_helper_that_fills_the_volumes_is_created_inert_from_the_tasks_image_and_removed_afterwards()
    {
        await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        var helper = _docker.Created.Single(created => created.Labels!["hephaestus.role"] == "file-fill");
        Assert.Equal("none", helper.HostConfig!.NetworkMode);
        Assert.Equal(["ALL"], helper.HostConfig!.CapDrop!);
        Assert.Equal("registry.test/app@sha256:" + new string('a', 64), helper.Image);
        Assert.All(helper.HostConfig!.Mounts!, mount => Assert.True(mount.VolumeOptions!.NoCopy));
        Assert.DoesNotContain($"start id-{helper.Name}", _docker.Calls);
        Assert.Contains($"id-{helper.Name}", _docker.Removed);
    }

    [Fact]
    public async Task A_helper_left_by_a_crash_is_replaced_by_name()
    {
        await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        Assert.Contains($"remove hephaestus-file-fill-{StepRunId}", _docker.Calls);
        Assert.True(_docker.Calls.IndexOf($"remove hephaestus-file-fill-{StepRunId}") < _docker.Calls.IndexOf($"create container hephaestus-file-fill-{StepRunId}"));
    }

    [Fact]
    public async Task A_step_with_no_methodology_files_places_only_the_context()
    {
        await Runtime().StartStepAsync(Request(methodology: []), Key, TestContext.Current.CancellationToken);

        Assert.Equal(["/context"], _docker.Extractions.Select(extraction => extraction.Parameters.Path));
    }

    [Fact]
    public async Task A_failure_to_place_the_files_removes_the_helper_and_starts_nothing()
    {
        _docker.ExtractFails = new IOException("disk full");

        await Assert.ThrowsAsync<IOException>(() => Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken));

        Assert.Contains($"id-hephaestus-file-fill-{StepRunId}", _docker.Removed);
        Assert.Empty(_docker.Started);
        Assert.DoesNotContain(_docker.Created, created => created.Labels!["hephaestus.role"] == "step");
    }

    [Fact]
    public async Task A_helper_that_cannot_be_removed_does_not_hide_how_the_work_went()
    {
        _docker.RemoveFails = container => container.StartsWith("id-", StringComparison.Ordinal) ? new DockerApiException(System.Net.HttpStatusCode.InternalServerError, "busy") : null;

        var started = await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        Assert.True(started.Created);
    }

    private static string[] EntryNames(byte[] tar)
    {
        using var reader = new TarReader(new MemoryStream(tar));
        var names = new List<string>();
        while (reader.GetNextEntry() is { } entry)
        {
            names.Add(entry.Name.TrimEnd('/'));
        }

        return [.. names];
    }

    [Fact]
    public async Task A_container_that_is_started_is_handed_to_the_watcher_with_the_deadline_of_the_request_and_the_call_does_not_wait_for_it()
    {
        var deadline = Deadline.AddMinutes(17);

        var started = await Runtime().StartStepAsync(Request(deadline: deadline), Key, TestContext.Current.CancellationToken);

        var watched = Assert.Single(_watcher.Watched);
        Assert.Equal(new WatchedStep(TaskId, StepRunId, started.ContainerId, deadline), watched);
        Assert.DoesNotContain(_docker.Calls, call => call.StartsWith("inspect", StringComparison.Ordinal) || call.StartsWith("logs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_container_that_a_crash_left_is_handed_to_the_watcher_too_so_that_its_end_is_reported_after_a_restart_of_the_call()
    {
        _docker.Existing.Add(new ContainerListResponse { ID = "existing", Names = [$"/hephaestus-step-{StepRunId}"], State = "running" });

        await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        Assert.Equal("existing", Assert.Single(_watcher.Watched).ContainerId);
    }

    [Fact]
    public async Task A_container_that_cannot_be_started_is_not_watched()
    {
        _docker.StartFails = DockerDouble.NotFound();

        await Assert.ThrowsAsync<DockerApiException>(() => Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken));

        Assert.Empty(_watcher.Watched);
    }
}
