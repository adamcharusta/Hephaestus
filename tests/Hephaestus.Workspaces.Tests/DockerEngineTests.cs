using System.Net;
using System.Net.Sockets;
using System.Text;
using Docker.DotNet.Models;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces.Tests;

/// <summary>
/// Story 3.2 against a real Docker Engine (AD-5, AD-6, AD-7, AD-8, AD-18): the container that is created is the one the definition describes, a step
/// user can write to its workspace on a glibc and on a musl image, and a process inside reaches nothing of the host. The tests are skipped where no Engine
/// answers and run in CI; each uses an installation of its own and leaves nothing behind.
/// </summary>
public sealed class DockerEngineTests(EngineFixture engine) : IClassFixture<EngineFixture>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private StepContainerRequest Request(Guid taskId, Guid stepRunId, ResolvedImage image, IReadOnlyList<string> command, IReadOnlyDictionary<string, string>? environment = null) =>
        new(
            taskId,
            stepRunId,
            image,
            command,
            environment ?? new Dictionary<string, string>(),
            new StepLimits(1500, 512L * 1024 * 1024),
            [new WorkspaceFile("instruction.md", Encoding.UTF8.GetBytes("Do the step."))],
            [new WorkspaceFile("manifest.yml", Encoding.UTF8.GetBytes("name: test"))]);

    private async Task<(long ExitCode, string Output)> RunToEndAsync(string container)
    {
        var wait = await engine.Docker.Containers.WaitContainerAsync(container, Cancel);
        using var logs = await engine.Docker.Containers.GetContainerLogsAsync(container, new ContainerLogsParameters { ShowStdout = true, ShowStderr = true }, Cancel);
        var (output, error) = await logs.ReadOutputToEndAsync(Cancel);
        return (wait.StatusCode, output + error);
    }

    private async Task<(ResolvedImage Image, Guid TaskId)> PrepareAsync(string imageName)
    {
        var image = await engine.Runtime.ResolveImageAsync(imageName, Cancel);
        var taskId = Guid.CreateVersion7();
        await engine.Runtime.EnsureTaskVolumeAsync(taskId, image, "task:g1:volume:0", Cancel);
        return (image, taskId);
    }

    [Fact]
    public void A_Docker_Engine_answers_wherever_the_tests_that_need_one_are_required_to_run()
    {
        Assert.True(RealEngine.Available || !RealEngine.Required, "GitHub Actions must be able to reach a Docker Engine: the tests that show a step container is hardened would otherwise be skipped.");
    }

    [Theory(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    [InlineData(EngineFixture.Musl, CLibrary.Musl)]
    [InlineData(EngineFixture.Glibc, CLibrary.Glibc)]
    public async Task The_C_library_of_a_real_image_is_established_without_running_it(string imageName, CLibrary expected)
    {
        var image = await engine.Runtime.ResolveImageAsync(imageName, Cancel);

        Assert.Equal(expected, image.Libc);
        Assert.Equal(engine.HostArchitecture, image.Architecture);
        Assert.StartsWith("sha256:", image.Digest, StringComparison.Ordinal);
        Assert.Contains(image.Digest, image.Reference, StringComparison.Ordinal);
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task An_image_that_does_not_exist_stops_the_task_and_says_why()
    {
        var refusal = await Assert.ThrowsAsync<ImageNotUsableException>(() => engine.Runtime.ResolveImageAsync("hephaestus.invalid/none:0", Cancel));

        Assert.Equal(Hephaestus.Core.Localization.MessageIds.WorkspaceImageUnavailable, refusal.Info.Id);
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task The_step_container_that_is_created_is_the_one_the_definition_describes()
    {
        var (image, taskId) = await PrepareAsync(EngineFixture.Musl);
        var stepRunId = Guid.CreateVersion7();

        var started = await engine.Runtime.StartStepAsync(Request(taskId, stepRunId, image, ["/bin/sleep", "120"]), "task:s1:container:0", Cancel);
        var inspected = await engine.Docker.Containers.InspectContainerAsync(started.ContainerId, Cancel);

        // The call returned while the step was still running: it did not wait for the step to end.
        Assert.True(inspected.State!.Running);
        Assert.True(started.Created);
        Assert.StartsWith("hephaestus-", inspected.Name.TrimStart('/'), StringComparison.Ordinal);
        Assert.Equal("10001:10001", inspected.Config!.User);
        Assert.Equal(["ALL"], inspected.HostConfig!.CapDrop!);
        Assert.Contains(inspected.HostConfig!.SecurityOpt!, option => option.StartsWith("no-new-privileges", StringComparison.Ordinal));
        Assert.DoesNotContain(inspected.HostConfig!.SecurityOpt!, option => option.Contains("seccomp", StringComparison.Ordinal) || option.Contains("unconfined", StringComparison.Ordinal));
        Assert.False(inspected.HostConfig!.Privileged);
        Assert.Equal(512, inspected.HostConfig!.PidsLimit);
        Assert.Equal(512L * 1024 * 1024, inspected.HostConfig!.Memory);
        Assert.Equal(1_500_000_000, inspected.HostConfig!.NanoCPUs);
        Assert.Empty(inspected.HostConfig!.PortBindings! ?? new Dictionary<string, IList<PortBinding>>());
        Assert.Empty(inspected.NetworkSettings!.Ports! ?? new Dictionary<string, IList<PortBinding>>());
        Assert.Equal(engine.WorkspaceNetwork, inspected.HostConfig!.NetworkMode);
        Assert.Equal(engine.InstallationId, inspected.Config!.Labels!["hephaestus.installation"]);
        Assert.Equal(taskId.ToString(), inspected.Config!.Labels!["hephaestus.task"]);
        Assert.Equal(stepRunId.ToString(), inspected.Config!.Labels!["hephaestus.step-run"]);
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task The_mounts_of_the_container_are_exactly_the_ones_the_layout_names()
    {
        var (image, taskId) = await PrepareAsync(EngineFixture.Musl);
        var stepRunId = Guid.CreateVersion7();

        var started = await engine.Runtime.StartStepAsync(Request(taskId, stepRunId, image, ["/bin/sleep", "120"]), "task:s2:container:0", Cancel);
        var inspected = await engine.Docker.Containers.InspectContainerAsync(started.ContainerId, Cancel);

        var mounts = inspected.Mounts!.Where(mount => mount.Type != "tmpfs").ToDictionary(mount => mount.Destination!);
        Assert.Equal(
            ["/hephaestus/agents/certs", "/hephaestus/agents/claude-code", "/hephaestus/agents/codex-cli", "/hephaestus/context", "/hephaestus/methodology", "/workspace"],
            mounts.Keys.Order(StringComparer.Ordinal));
        Assert.All(mounts.Values, mount => Assert.Equal("volume", mount.Type));
        Assert.Equal($"hephaestus-ws-{taskId}", mounts["/workspace"].Name);
        Assert.True(mounts["/workspace"].RW);
        Assert.All(mounts.Where(mount => mount.Key != "/workspace").Select(mount => mount.Value), mount => Assert.False(mount.RW));
        Assert.Equal(["/hephaestus/home"], inspected.HostConfig!.Tmpfs!.Keys);
        Assert.DoesNotContain(inspected.Mounts!, mount => mount.Type == "bind");
    }

    [Theory(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    [InlineData(EngineFixture.Musl, "musl")]
    [InlineData(EngineFixture.Glibc, "glibc")]
    public async Task The_step_user_can_write_its_workspace_and_sees_the_build_that_matches_the_image(string imageName, string libc)
    {
        var (image, taskId) = await PrepareAsync(imageName);
        var stepRunId = Guid.CreateVersion7();
        const string script = """
            set -e
            touch /workspace/created
            mkdir /workspace/sub && touch /workspace/sub/created
            touch /hephaestus/home/created
            echo "user $(id -u):$(id -g)"
            # What the kernel says about the process: no capability, no new privileges, and a seccomp filter (Docker's default profile) in force.
            echo "kernel $(grep -E '^(CapEff|NoNewPrivs|Seccomp):' /proc/self/status | tr -d '[:blank:]' | tr '\n' ' ')"
            echo "home $HOME"
            echo "claude: $(tail -n 1 /hephaestus/agents/claude-code/claude)"
            echo "codex: $(ls /hephaestus/agents/codex-cli | tr '\n' ' ')"
            echo "context: $(cat /hephaestus/context/instruction.md)"
            echo "methodology: $(cat /hephaestus/methodology/manifest.yml)"
            echo "certs: $(wc -c < /hephaestus/agents/certs/ca-certificates.crt)"
            for dir in /hephaestus/agents/claude-code /hephaestus/context /hephaestus/methodology /hephaestus/agents/certs; do
              if touch "$dir/planted" 2>/dev/null; then echo "WRITABLE $dir"; fi
            done
            """;

        var started = await engine.Runtime.StartStepAsync(Request(taskId, stepRunId, image, ["/bin/sh", "-c", script]), "task:s3:container:0", Cancel);
        var (exit, output) = await RunToEndAsync(started.ContainerId);

        Assert.Equal(0, exit);
        Assert.Contains("user 10001:10001", output, StringComparison.Ordinal);
        Assert.Contains("home /hephaestus/home", output, StringComparison.Ordinal);
        Assert.Contains("CapEff:0000000000000000 NoNewPrivs:1 Seccomp:2", output, StringComparison.Ordinal);
        Assert.Contains($"claude-code {engine.HostArchitecture.Tag()}-{libc} claude", output, StringComparison.Ordinal);
        Assert.Contains($"codex: codex codex-code-mode-host", output, StringComparison.Ordinal);
        Assert.Contains("context: Do the step.", output, StringComparison.Ordinal);
        Assert.Contains("methodology: name: test", output, StringComparison.Ordinal);
        Assert.DoesNotContain("WRITABLE", output, StringComparison.Ordinal);
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task Nothing_of_the_host_is_reachable_from_inside_a_step_container()
    {
        var (image, taskId) = await PrepareAsync(EngineFixture.Musl);
        using var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var gateway = await HostAddressAsync(image, port);
        const string script = """
            for path in /var/run/docker.sock /run/docker.sock /run/hephaestus /var/run/hephaestus /var/lib/hephaestus /etc/hephaestus; do
              if [ -e "$path" ]; then echo "PRESENT $path"; fi
            done
            if nc -z -w 3 "$HOST_ADDRESS" "$HOST_PORT" 2>/dev/null; then echo "REACHED host"; fi
            if nc -z -w 3 127.0.0.1 "$HOST_PORT" 2>/dev/null; then echo "REACHED loopback"; fi
            echo finished
            """;
        var environment = new Dictionary<string, string> { ["HOST_ADDRESS"] = gateway, ["HOST_PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture) };

        var started = await engine.Runtime.StartStepAsync(Request(taskId, Guid.CreateVersion7(), image, ["/bin/sh", "-c", script], environment), "task:s4:container:0", Cancel);
        var (exit, output) = await RunToEndAsync(started.ContainerId);

        Assert.Equal(0, exit);
        Assert.Contains("finished", output, StringComparison.Ordinal);
        Assert.DoesNotContain("PRESENT", output, StringComparison.Ordinal);
        Assert.DoesNotContain("REACHED", output, StringComparison.Ordinal);
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task A_second_start_of_the_same_step_run_finds_the_container_and_makes_no_second_one()
    {
        var (image, taskId) = await PrepareAsync(EngineFixture.Musl);
        var stepRunId = Guid.CreateVersion7();
        var request = Request(taskId, stepRunId, image, ["/bin/sleep", "120"]);

        var first = await engine.Runtime.StartStepAsync(request, "task:s5:container:0", Cancel);
        var second = await engine.Runtime.StartStepAsync(request, "task:s5:container:0", Cancel);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.ContainerId, second.ContainerId);
        var found = await engine.Docker.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>> { ["label"] = new Dictionary<string, bool> { [$"hephaestus.step-run={stepRunId}"] = true, ["hephaestus.role=step"] = true } },
            },
            Cancel);
        Assert.Single(found);
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task A_container_that_was_created_and_never_started_is_started_by_the_next_call()
    {
        var (image, taskId) = await PrepareAsync(EngineFixture.Musl);
        var stepRunId = Guid.CreateVersion7();
        var request = Request(taskId, stepRunId, image, ["/bin/sleep", "120"]);
        var first = await engine.Runtime.StartStepAsync(request, "task:s6:container:0", Cancel);
        await engine.Docker.Containers.KillContainerAsync(first.ContainerId, new ContainerKillParameters(), Cancel);
        await engine.Docker.Containers.RemoveContainerAsync(first.ContainerId, new ContainerRemoveParameters(), Cancel);
        // What a crash between the creation and the start leaves: a container that exists and has not run.
        var left = await engine.Docker.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Name = $"hephaestus-step-{stepRunId}",
                Image = image.Reference,
                Labels = new Dictionary<string, string> { ["hephaestus.installation"] = engine.InstallationId, ["hephaestus.step-run"] = stepRunId.ToString(), ["hephaestus.role"] = "step" },
                Entrypoint = ["/bin/sleep"],
                Cmd = ["120"],
            },
            Cancel);

        var second = await engine.Runtime.StartStepAsync(request, "task:s6:container:0", Cancel);

        Assert.False(second.Created);
        Assert.Equal(left.ID, second.ContainerId);
        Assert.True((await engine.Docker.Containers.InspectContainerAsync(left.ID, Cancel)).State!.Running);
    }

    /// <summary>
    /// The address, as a container on Docker's default network sees it, at which this machine answers on <paramref name="port"/>; it is the same
    /// address that a step container must fail to reach. A machine on which no container reaches it is a machine on which the test proves nothing.
    /// </summary>
    private async Task<string> HostAddressAsync(ResolvedImage image, int port)
    {
        var created = await engine.Docker.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Name = $"hephaestus-control-{Guid.NewGuid():N}",
                Image = image.Reference,
                Labels = new Dictionary<string, string> { ["hephaestus.installation"] = engine.InstallationId },
                Entrypoint = ["/bin/sh"],
                Cmd = ["-c", "ip route | awk '/default/ {print $3}'"],
                HostConfig = new HostConfig(),
            },
            Cancel);
        await engine.Docker.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), Cancel);
        var (_, output) = await RunToEndAsync(created.ID);
        var address = output.Trim();
        Unless(IPAddress.TryParse(address, out _), "The default network of Docker names no gateway on this machine.");

        var probe = await engine.Docker.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Name = $"hephaestus-control-{Guid.NewGuid():N}",
                Image = image.Reference,
                Labels = new Dictionary<string, string> { ["hephaestus.installation"] = engine.InstallationId },
                Entrypoint = ["/bin/sh"],
                Cmd = ["-c", $"nc -z -w 3 {address} {port} && echo REACHED"],
            },
            Cancel);
        await engine.Docker.Containers.StartContainerAsync(probe.ID, new ContainerStartParameters(), Cancel);
        var (_, reached) = await RunToEndAsync(probe.ID);
        Unless(reached.Contains("REACHED", StringComparison.Ordinal), "An ordinary container cannot reach a listener of this machine, so the test would prove nothing.");
        return address;
    }

    /// <summary>What a test needs of this machine that it cannot make: where continuous integration runs it, a failure, because a test that skipped proves nothing; elsewhere, a skip.</summary>
    private static void Unless(bool condition, string reason)
    {
        if (condition)
        {
            return;
        }

        if (RealEngine.Required)
        {
            Assert.Fail(reason);
        }

        Assert.Skip(reason);
    }
}
