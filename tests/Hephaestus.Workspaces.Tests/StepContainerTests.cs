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
        Runtimes.Of(_docker.Client, new WorkspaceRuntimeOptions(Installation, _vendor.Lock, architecture), _watcher);

    private static ResolvedImage Image(CLibrary libc = CLibrary.Glibc, AgentArchitecture architecture = AgentArchitecture.X64) =>
        new("registry.test/app@sha256:" + new string('a', 64), "sha256:" + new string('a', 64), architecture, libc);

    private static StepContainerRequest Request(
        ResolvedImage? image = null,
        StepLimits? limits = null,
        IReadOnlyList<string>? command = null,
        IReadOnlyDictionary<string, string>? environment = null,
        IReadOnlyList<WorkspaceFile>? context = null,
        IReadOnlyList<WorkspaceFile>? methodology = null,
        DateTimeOffset? deadline = null,
        IReadOnlyList<string>? allowedHosts = null) =>
        new(
            TaskId,
            StepRunId,
            image ?? Image(),
            command ?? ["/hephaestus/agents/claude-code/claude", "-p", "Read /hephaestus/context/instruction.md"],
            environment ?? new Dictionary<string, string> { ["ANTHROPIC_MODEL"] = "model" },
            limits ?? new StepLimits(2500, 3L * 1024 * 1024 * 1024),
            context ?? [new WorkspaceFile("instruction.md", [1])],
            methodology ?? [new WorkspaceFile("manifest.yml", [2])],
            deadline ?? Deadline,
            allowedHosts);

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
    public async Task The_container_is_attached_to_the_internal_network_of_its_step_run_alone_and_is_not_restarted_by_docker()
    {
        var host = (await StartedAsync()).HostConfig!;

        Assert.Equal($"hephaestus-net-{StepRunId}", host.NetworkMode);
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

        Assert.Equal(["TOKEN_FILE=/x", .. EgressEnvironment.Variables.Select(pair => $"{pair.Key}={pair.Value}"), "HOME=/hephaestus/home"], container.Env!);
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
        Assert.Equal([$"id-hephaestus-egress-{StepRunId}", started.ContainerId], _docker.Started);
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
        Assert.Equal([$"id-hephaestus-egress-{StepRunId}", "winner"], _docker.Started);
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
        Assert.DoesNotContain(_docker.Started, started => started.Contains("-step-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_step_runs_network_is_created_internal_when_it_is_missing_and_the_outbound_network_with_a_route_out()
    {
        await StartedAsync();

        Assert.Equal(2, _docker.Networks.Count);
        var outbound = _docker.Networks.Single(network => network.Name == "hephaestus-outbound");
        Assert.False(outbound.Internal);
        Assert.Equal(string.Empty, outbound.Labels!["hephaestus.step-run"]);
        var step = _docker.Networks.Single(network => network.Name == $"hephaestus-net-{StepRunId}");
        Assert.True(step.Internal);
        Assert.Equal("bridge", step.Driver);
        Assert.Equal(Installation, step.Labels!["hephaestus.installation"]);
        Assert.Equal(TaskId.ToString(), step.Labels!["hephaestus.task"]);
        Assert.Equal(StepRunId.ToString(), step.Labels!["hephaestus.step-run"]);
        Assert.Equal(Key, step.Labels!["hephaestus.effect"]);
    }

    [Fact]
    public async Task A_step_runs_network_that_has_a_route_out_is_never_used()
    {
        _docker.NetworkExists = true;
        _docker.NetworkInternal = false;

        var refusal = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken));

        Assert.Equal(Hephaestus.Core.Localization.MessageIds.WorkspaceNetworkNotInternal, refusal.Info.Id);
        Assert.Equal([$"hephaestus-net-{StepRunId}"], refusal.Info.Arguments);
        Assert.Empty(_docker.Created);
        Assert.Empty(_docker.Started);
    }

    [Fact]
    public async Task A_network_that_something_else_made_in_between_is_checked_too()
    {
        _docker.NetworkCreateFails = DockerDouble.Conflict();
        _docker.NetworkInternal = false;
        var inspections = 0;
        _docker.Client.Networks.InspectNetworkAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            ++inspections % 2 == 1
                ? Task.FromException<NetworkResponse>(DockerDouble.NotFound())
                : Task.FromResult(new NetworkResponse { Name = call.Arg<string>(), Internal = false }));

        await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken));

        Assert.Empty(_docker.Started);
    }

    [Fact]
    public async Task A_container_that_cannot_start_is_removed_so_that_a_retry_makes_a_new_one()
    {
        _docker.StartFailsFor = container => container.Contains("-step-", StringComparison.Ordinal) ? new DockerApiException(System.Net.HttpStatusCode.BadRequest, "executable file not found") : null;

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
    public async Task The_networks_are_left_alone_when_they_exist()
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
        _docker.Client.Networks.InspectNetworkAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            ++inspections % 2 == 1
                ? Task.FromException<NetworkResponse>(DockerDouble.NotFound())
                : Task.FromResult(new NetworkResponse { Name = call.Arg<string>(), Internal = !call.Arg<string>().StartsWith("hephaestus-outbound", StringComparison.Ordinal) }));

        var started = await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        Assert.True(started.Created);
    }

    private CreateContainerParameters Proxy() => _docker.Created.Single(created => created.Name == $"hephaestus-egress-{StepRunId}");

    [Fact]
    public async Task The_egress_proxy_is_a_separate_unprivileged_container_of_the_hephaestus_image_with_no_mount_no_credential_and_no_socket()
    {
        await StartedAsync();
        var proxy = Proxy();
        var host = proxy.HostConfig!;

        Assert.Equal("ghcr.io/adamcharusta/hephaestus:latest", proxy.Image);
        Assert.Equal(["hephaestus", "egress-proxy"], proxy.Entrypoint!);
        Assert.Equal("10001:10001", proxy.User);
        Assert.Equal(["ALL"], host.CapDrop!);
        Assert.Equal(["no-new-privileges"], host.SecurityOpt!);
        Assert.False(host.Privileged);
        Assert.True(host.ReadonlyRootfs);
        Assert.False(host.PublishAllPorts);
        Assert.Empty(host.Mounts! ?? []);
        Assert.Empty(host.Binds! ?? []);
        Assert.Equal(["/tmp"], host.Tmpfs!.Keys);
        Assert.Equal(512, host.PidsLimit);
        Assert.True(host.Memory > 0);
        Assert.Equal(host.Memory, host.MemorySwap);
        Assert.Equal(RestartPolicyKind.No, host.RestartPolicy!.Name);
        Assert.DoesNotContain(proxy.Env!, variable => variable.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) || variable.Contains("SECRET", StringComparison.OrdinalIgnoreCase) || variable.Contains("KEY", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("egress-proxy", proxy.Labels!["hephaestus.role"]);
        Assert.Equal(StepRunId.ToString(), proxy.Labels!["hephaestus.step-run"]);
        Assert.Equal(TaskId.ToString(), proxy.Labels!["hephaestus.task"]);
        Assert.Equal(Installation, proxy.Labels!["hephaestus.installation"]);
    }

    [Fact]
    public async Task The_proxy_is_created_on_the_outbound_network_and_joins_the_step_runs_network_under_the_fixed_name_before_it_starts()
    {
        await StartedAsync();

        Assert.Equal("hephaestus-outbound", Proxy().HostConfig!.NetworkMode);
        var (network, parameters) = Assert.Single(_docker.Connections);
        Assert.Equal($"hephaestus-net-{StepRunId}", network);
        Assert.Equal($"id-hephaestus-egress-{StepRunId}", parameters.Container);
        Assert.Equal(["hephaestus-egress"], parameters.EndpointConfig!.Aliases!);
        var order = _docker.Calls.Where(call => call.StartsWith("connect", StringComparison.Ordinal) || call.StartsWith("start", StringComparison.Ordinal) || call.StartsWith("create network", StringComparison.Ordinal) || call.StartsWith("create container hephaestus-", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            [
                "create network hephaestus-outbound",
                $"create network hephaestus-net-{StepRunId}",
                $"create container hephaestus-egress-{StepRunId}",
                $"connect id-hephaestus-egress-{StepRunId} hephaestus-net-{StepRunId}",
                $"start id-hephaestus-egress-{StepRunId}",
                $"create container hephaestus-file-fill-{StepRunId}",
                $"create container hephaestus-step-{StepRunId}",
                $"start id-hephaestus-step-{StepRunId}",
            ],
            order);
    }

    [Fact]
    public async Task The_step_container_is_created_only_after_the_proxy_says_it_is_listening()
    {
        var reads = 0;
        _docker.ProxyLogs = (_, _) => Task.FromResult(++reads < 3 ? DockerDouble.Output() : DockerDouble.Output((1, "listening 3128\n")));

        await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        Assert.Equal(3, reads);
        Assert.True(_docker.Calls.IndexOf($"logs id-hephaestus-egress-{StepRunId}") < _docker.Calls.IndexOf($"create container hephaestus-step-{StepRunId}"));
    }

    [Fact]
    public async Task A_proxy_that_stops_before_it_listens_stops_the_step_and_says_why_and_is_removed()
    {
        _docker.ProxyLogs = (_, _) => Task.FromResult(DockerDouble.Output((2, "crashed\n")));
        _docker.Inspector = (id, _) => Task.FromResult(DockerDouble.Inspected(id.Contains("egress", StringComparison.Ordinal) ? "exited" : "running", exitCode: 1));

        var refusal = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken));

        Assert.Equal(Hephaestus.Core.Localization.MessageIds.WorkspaceEgressProxyNotReady, refusal.Info.Id);
        Assert.Equal(["ghcr.io/adamcharusta/hephaestus:latest"], refusal.Info.Arguments);
        Assert.Contains($"id-hephaestus-egress-{StepRunId}", _docker.Removed);
        Assert.DoesNotContain(_docker.Created, created => created.Name == $"hephaestus-step-{StepRunId}");
    }

    [Fact]
    public async Task A_proxy_that_said_it_was_listening_and_then_ended_stops_the_step()
    {
        _docker.Inspector = (id, _) => Task.FromResult(DockerDouble.Inspected(id.Contains("egress", StringComparison.Ordinal) ? "exited" : "running", exitCode: 139));

        var refusal = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken));

        Assert.Equal(Hephaestus.Core.Localization.MessageIds.WorkspaceEgressProxyNotReady, refusal.Info.Id);
        Assert.DoesNotContain(_docker.Created, created => created.Name == $"hephaestus-step-{StepRunId}");
    }

    [Fact]
    public async Task An_outbound_network_that_is_internal_is_never_used_because_no_proxy_on_it_would_have_a_route_out()
    {
        _docker.NetworkExists = true;
        _docker.Client.Networks.InspectNetworkAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult(new NetworkResponse { Name = call.Arg<string>(), Internal = true }));

        var refusal = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken));

        Assert.Equal(Hephaestus.Core.Localization.MessageIds.WorkspaceNetworkNotOutbound, refusal.Info.Id);
        Assert.Equal(["hephaestus-outbound"], refusal.Info.Arguments);
        Assert.Empty(_docker.Created);
    }

    [Fact]
    public async Task A_found_container_that_has_not_started_gets_its_proxy_back_before_it_starts_when_the_crash_took_the_proxy_too()
    {
        _docker.Existing.Add(new ContainerListResponse { ID = "left", Names = [$"/hephaestus-step-{StepRunId}"], State = "created" });
        _docker.Inspector = (id, _) => id.Contains("egress", StringComparison.Ordinal) && !id.StartsWith("id-", StringComparison.Ordinal)
            ? Task.FromException<ContainerInspectResponse>(DockerDouble.NotFound())
            : Task.FromResult(DockerDouble.Inspected("running"));

        var started = await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        Assert.False(started.Created);
        Assert.Equal([$"id-hephaestus-egress-{StepRunId}", "left"], _docker.Started);
        Assert.Single(_docker.Created, created => created.Name == $"hephaestus-egress-{StepRunId}");
        Assert.DoesNotContain(_docker.Created, created => created.Name == $"hephaestus-step-{StepRunId}");
    }

    [Fact]
    public async Task A_found_container_that_has_not_started_keeps_its_proxy_when_it_still_runs()
    {
        _docker.Existing.Add(new ContainerListResponse { ID = "left", Names = [$"/hephaestus-step-{StepRunId}"], State = "created" });

        var started = await Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken);

        Assert.False(started.Created);
        Assert.Equal(["left"], _docker.Started);
        Assert.Empty(_docker.Created);
    }

    [Fact]
    public async Task A_proxy_that_does_not_listen_within_the_limit_stops_the_step()
    {
        _docker.ProxyLogs = (_, _) => Task.FromResult(DockerDouble.Output());
        var runtime = Runtimes.Of(_docker.Client, new WorkspaceRuntimeOptions(Installation, _vendor.Lock, AgentArchitecture.X64) { EgressProxyReadyLimit = TimeSpan.FromMilliseconds(50), EgressProxyPollInterval = TimeSpan.FromMilliseconds(10) }, _watcher);

        var refusal = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => runtime.StartStepAsync(Request(), Key, TestContext.Current.CancellationToken));

        Assert.Equal(Hephaestus.Core.Localization.MessageIds.WorkspaceEgressProxyNotReady, refusal.Info.Id);
        Assert.Contains($"id-hephaestus-egress-{StepRunId}", _docker.Removed);
    }

    [Fact]
    public async Task A_proxy_image_that_is_not_on_this_machine_stops_the_step_and_names_the_image()
    {
        _docker.FailCreate = parameters => parameters.Name!.StartsWith("hephaestus-egress-", StringComparison.Ordinal) ? DockerDouble.NotFound() : null;

        var refusal = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => Runtime().StartStepAsync(Request(), Key, TestContext.Current.CancellationToken));

        Assert.Equal(Hephaestus.Core.Localization.MessageIds.WorkspaceEgressProxyNotReady, refusal.Info.Id);
        Assert.Empty(_docker.Started);
    }

    [Fact]
    public async Task A_proxy_that_a_crash_left_is_replaced_because_it_holds_nothing()
    {
        await StartedAsync();

        Assert.Contains($"remove hephaestus-egress-{StepRunId}", _docker.Calls);
        Assert.True(_docker.Calls.IndexOf($"remove hephaestus-egress-{StepRunId}") < _docker.Calls.IndexOf($"create container hephaestus-egress-{StepRunId}"));
    }

    [Fact]
    public async Task The_proxy_gets_the_step_runs_allowed_hosts_in_its_environment_and_nothing_when_any_public_host_may_be_reached()
    {
        await StartedAsync(Request(allowedHosts: ["api.nuget.org", "Registry.npmjs.org"]));
        Assert.Contains("HEPHAESTUS_EGRESS_ALLOWED_HOSTS=api.nuget.org,registry.npmjs.org", Proxy().Env!);

        _docker.Created.Clear();
        await StartedAsync(Request(allowedHosts: null));
        Assert.DoesNotContain(Proxy().Env!, variable => variable.StartsWith("HEPHAESTUS_EGRESS_ALLOWED_HOSTS", StringComparison.Ordinal));

        _docker.Created.Clear();
        await StartedAsync(Request(allowedHosts: []));
        Assert.Contains("HEPHAESTUS_EGRESS_ALLOWED_HOSTS=", Proxy().Env!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("api.nuget.org,evil.example")]
    [InlineData("has space.example")]
    [InlineData("http://example.com")]
    [InlineData("example.com/path")]
    public async Task A_host_that_is_not_a_host_name_is_refused_before_anything_is_created(string host)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Runtime().StartStepAsync(Request(allowedHosts: [host]), Key, TestContext.Current.CancellationToken));

        Assert.Empty(_docker.Calls);
    }

    [Fact]
    public async Task The_proxy_variables_and_the_git_rewrite_are_set_in_every_step_container_and_replace_what_the_invocation_sets()
    {
        var container = await StartedAsync(Request(environment: new Dictionary<string, string> { ["HTTPS_PROXY"] = "http://evil.example:1", ["no_proxy"] = "*", ["GIT_CONFIG_COUNT"] = "9", ["X"] = "1" }));

        Assert.Equal(["X=1", .. EgressEnvironment.Variables.Select(pair => $"{pair.Key}={pair.Value}"), "HOME=/hephaestus/home"], container.Env!);
        Assert.Contains("HTTPS_PROXY=http://hephaestus-egress:3128", container.Env!);
        Assert.Contains("NO_PROXY=localhost,127.0.0.1,::1", container.Env!);
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
        Assert.Equal([$"id-hephaestus-egress-{StepRunId}"], _docker.Started);
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
        // The proxy is looked at until it listens; the step container is not looked at by the call, only by the watcher.
        Assert.DoesNotContain(_docker.Calls, call => call == $"inspect {started.ContainerId}" || call == $"logs {started.ContainerId}");
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
