using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Docker.DotNet.Models;
using Hephaestus.Core.Workspaces;
using NSubstitute;

namespace Hephaestus.Workspaces.Tests;

/// <summary>
/// Story 3.4 against a real Docker Engine (AD-7, AR-41, AR-47, T7, T12, T14): each step run has an internal network of its own with the egress proxy as its only exit; a connection
/// that skips the proxy fails; the proxy refuses what is not public and what the Project did not list, and the runtime records the refusal; the proxy container is what AR-41 says. The tests are
/// skipped where no Engine answers and run in CI, where the <c>hephaestus</c> image built for the run is named by <see cref="EngineFixture.ProxyImageVariable"/>.
/// </summary>
public sealed class EgressProxyEngineTests(EngineFixture engine) : IClassFixture<EngineFixture>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>A public host that answers plain HTTP and HTTPS; the tests that reach it need the network of the machine, which CI has.</summary>
    private const string PublicHost = "example.com";

    private StepContainerRequest Request(Guid taskId, Guid stepRunId, ResolvedImage image, string script, IReadOnlyList<string>? allowedHosts, IReadOnlyDictionary<string, string>? environment = null) =>
        new(
            taskId,
            stepRunId,
            image,
            ["/bin/sh", "-c", script],
            environment ?? new Dictionary<string, string>(),
            new StepLimits(1500, 512L * 1024 * 1024),
            [new WorkspaceFile("instruction.md", Encoding.UTF8.GetBytes("Do the step."))],
            [new WorkspaceFile("manifest.yml", Encoding.UTF8.GetBytes("name: test"))],
            DateTimeOffset.UtcNow.AddMinutes(5),
            allowedHosts);

    private async Task<(ResolvedImage Image, Guid TaskId)> PrepareAsync()
    {
        Unless(engine.ProxyImage is not null, $"{EngineFixture.ProxyImageVariable} names no hephaestus image to create the egress proxies from.");
        var image = await engine.Runtime.ResolveImageAsync(EngineFixture.Musl, Cancel);
        var taskId = Guid.CreateVersion7();
        await engine.Runtime.EnsureTaskVolumeAsync(taskId, image, "task:e:volume:0", Cancel);
        return (image, taskId);
    }

    private async Task<(long ExitCode, string Output)> RunToEndAsync(string container)
    {
        var wait = await engine.Docker.Containers.WaitContainerAsync(container, Cancel);
        var output = await ContainerLogs.ReadAsync(engine.Docker, container, Cancel);
        return (wait.StatusCode, output);
    }

    private async Task<string> ProxyLogAsync(Guid stepRunId) => await ContainerLogs.ReadAsync(engine.Docker, WorkspaceLabels.EgressProxy(stepRunId), Cancel);

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task The_step_container_is_on_its_step_runs_internal_network_alone_and_the_proxy_is_the_only_other_thing_on_it()
    {
        var (image, taskId) = await PrepareAsync();
        var stepRunId = Guid.CreateVersion7();

        var started = await engine.Runtime.StartStepAsync(Request(taskId, stepRunId, image, "sleep 120", null), "task:e1:container:0", Cancel);

        var step = await engine.Docker.Containers.InspectContainerAsync(started.ContainerId, Cancel);
        var networkName = $"hephaestus-net-{stepRunId}";
        Assert.Equal([networkName], step.NetworkSettings!.Networks!.Keys);
        var network = await engine.Docker.Networks.InspectNetworkAsync(networkName, Cancel);
        Assert.True(network.Internal);
        Assert.Equal(engine.InstallationId, network.Labels!["hephaestus.installation"]);
        Assert.Equal(stepRunId.ToString(), network.Labels!["hephaestus.step-run"]);
        var members = network.Containers!.Values.Select(member => member.Name).Order(StringComparer.Ordinal).ToList();
        Assert.Equal([$"hephaestus-egress-{stepRunId}", $"hephaestus-step-{stepRunId}"], members);
        var proxy = await engine.Docker.Containers.InspectContainerAsync($"hephaestus-egress-{stepRunId}", Cancel);
        Assert.Equal([networkName, engine.OutboundNetwork], proxy.NetworkSettings!.Networks!.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("hephaestus-egress", proxy.NetworkSettings!.Networks![networkName].Aliases!);
        Assert.False((await engine.Docker.Networks.InspectNetworkAsync(engine.OutboundNetwork, Cancel)).Internal);
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task The_proxy_is_a_separate_unprivileged_container_with_no_mount_no_credential_and_no_docker_socket()
    {
        var (image, taskId) = await PrepareAsync();
        var stepRunId = Guid.CreateVersion7();
        await engine.Runtime.StartStepAsync(Request(taskId, stepRunId, image, "sleep 120", null), "task:e2:container:0", Cancel);

        var proxy = await engine.Docker.Containers.InspectContainerAsync($"hephaestus-egress-{stepRunId}", Cancel);

        Assert.True(proxy.State!.Running);
        Assert.Equal("10001:10001", proxy.Config!.User);
        Assert.Equal(["ALL"], proxy.HostConfig!.CapDrop!);
        Assert.Contains(proxy.HostConfig!.SecurityOpt!, option => option.StartsWith("no-new-privileges", StringComparison.Ordinal));
        Assert.False(proxy.HostConfig!.Privileged);
        Assert.True(proxy.HostConfig!.ReadonlyRootfs);
        // Docker lists volumes and binds under Mounts and a tmpfs under HostConfig.Tmpfs alone: the proxy has none of the first and one of the second.
        Assert.Empty(proxy.Mounts! ?? []);
        Assert.Equal(["/tmp"], proxy.HostConfig!.Tmpfs!.Keys);
        Assert.Empty(proxy.NetworkSettings!.Ports! ?? new Dictionary<string, IList<PortBinding>>());
        Assert.Equal(engine.ProxyImage, proxy.Config!.Image);
        Assert.Equal(["hephaestus", "egress-proxy"], proxy.Path is "hephaestus" ? ["hephaestus", .. proxy.Args!] : proxy.Config!.Entrypoint!);
        // The image's own variables name directories (HEPHAESTUS_SECRETS_DIR among them) that the proxy has no volume for; what must not be there is a credential or its variable.
        Assert.DoesNotContain(proxy.Config!.Env!, variable => variable.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) || variable.Contains("API_KEY", StringComparison.OrdinalIgnoreCase) || variable.Contains("GITHUB", StringComparison.OrdinalIgnoreCase) || variable.StartsWith("ANTHROPIC", StringComparison.OrdinalIgnoreCase) || variable.StartsWith("OPENAI", StringComparison.OrdinalIgnoreCase));
        Assert.All(proxy.Config!.Env!, variable => Assert.True(variable.StartsWith("PATH=", StringComparison.Ordinal) || variable.StartsWith("HEPHAESTUS_", StringComparison.Ordinal) || variable.StartsWith("DOTNET_", StringComparison.Ordinal) || variable.StartsWith("ASPNET", StringComparison.Ordinal) || variable.StartsWith("APP_UID=", StringComparison.Ordinal), variable));
        Assert.Equal("egress-proxy", proxy.Config!.Labels!["hephaestus.role"]);
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task A_connection_that_skips_the_proxy_fails_and_the_proxy_refuses_the_host_the_dashboard_port_and_another_step_container()
    {
        var (image, taskId) = await PrepareAsync();
        var other = Guid.CreateVersion7();
        var otherStarted = await engine.Runtime.StartStepAsync(Request(taskId, other, image, "sleep 120", null), "task:e3:container:0", Cancel);
        var otherAddress = (await engine.Docker.Containers.InspectContainerAsync(otherStarted.ContainerId, Cancel)).NetworkSettings!.Networks!.Values.Single().IPAddress;
        var gateway = await HostAddressAsync(image);
        var stepRunId = Guid.CreateVersion7();
        // Busybox's wget speaks plain HTTP to a proxy and cannot tunnel, so HTTPS is asked for as a raw CONNECT through nc and judged by the proxy's first line.
        const string script = """
            echo "proxy: $HTTPS_PROXY no_proxy: $NO_PROXY"
            echo "--- direct, no proxy variables ---"
            if env -u http_proxy -u https_proxy -u HTTP_PROXY -u HTTPS_PROXY wget -T 8 -q -O /dev/null "http://$PUBLIC_HOST/" 2>/dev/null; then echo "REACHED direct"; fi
            if nc -z -w 3 "$HOST_ADDRESS" 8717 2>/dev/null; then echo "REACHED host directly"; fi
            if nc -z -w 3 "$OTHER_ADDRESS" 80 2>/dev/null; then echo "REACHED other directly"; fi
            if nc -z -w 3 "$OTHER_ADDRESS" 3128 2>/dev/null; then echo "REACHED other directly"; fi
            echo "--- through the proxy ---"
            for target in "http://$HOST_ADDRESS:8717/" "http://169.254.169.254/" "http://127.0.0.1:8717/" "http://$OTHER_ADDRESS/" "http://hephaestus-step-$OTHER_STEP/"; do
              code="$(wget -T 8 -S -q -O /dev/null "$target" 2>&1 | awk '/^  HTTP\//{print $2}' | tail -n 1)"
              echo "$target -> ${code:-none}"
            done
            for target in "$HOST_ADDRESS:8717" "10.0.0.1:443" "[::1]:443"; do
              line="$(printf 'CONNECT %s HTTP/1.1\r\n\r\n' "$target" | nc -w 8 hephaestus-egress 3128 | head -n 1 | tr -d '\r')"
              echo "CONNECT $target -> ${line:-none}"
            done
            echo finished
            """;
        var environment = new Dictionary<string, string> { ["HOST_ADDRESS"] = gateway, ["OTHER_ADDRESS"] = otherAddress!, ["OTHER_STEP"] = other.ToString("D"), ["PUBLIC_HOST"] = PublicHost };

        var started = await engine.Runtime.StartStepAsync(Request(taskId, stepRunId, image, script, null, environment), "task:e4:container:0", Cancel);
        var (exit, output) = await RunToEndAsync(started.ContainerId);

        Assert.Equal(0, exit);
        Assert.Contains("finished", output, StringComparison.Ordinal);
        Assert.Contains("proxy: http://hephaestus-egress:3128 no_proxy: localhost,127.0.0.1,::1", output, StringComparison.Ordinal);
        Assert.DoesNotContain("REACHED", output, StringComparison.Ordinal);
        var lines = output.Split('\n').Where(line => line.Contains(" -> ", StringComparison.Ordinal)).ToList();
        Assert.Equal(8, lines.Count);
        // Nothing was reached: an address is refused with 403; a name the proxy cannot resolve from its own network is a 502; neither is a 200.
        Assert.All(lines, line => Assert.DoesNotContain("200", line[line.IndexOf(" -> ", StringComparison.Ordinal)..], StringComparison.Ordinal));
        Assert.All(lines.Where(line => !line.Contains("hephaestus-step-", StringComparison.Ordinal)), line => Assert.Contains("403", line, StringComparison.Ordinal));

        var log = await ProxyLogAsync(stepRunId);
        Assert.Contains($"refused {gateway} private-address", log, StringComparison.Ordinal);
        Assert.Contains("refused 169.254.169.254 private-address", log, StringComparison.Ordinal);
        Assert.Contains($"refused {otherAddress} private-address", log, StringComparison.Ordinal);
        Assert.Contains("refused 10.0.0.1 private-address", log, StringComparison.Ordinal);
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task With_a_list_a_host_outside_it_is_refused_and_recorded_on_the_Signal_and_a_host_on_it_is_reached_over_https_and_plain_http()
    {
        var (image, taskId) = await PrepareAsync();
        var stepRunId = Guid.CreateVersion7();
        var script = $$"""
            tunnel() { printf 'CONNECT %s:443 HTTP/1.1\r\n\r\n' "$1" | nc -w 20 hephaestus-egress 3128 | head -n 1 | tr -d '\r'; }
            echo "allowed https: $(tunnel {{PublicHost}})"
            echo "allowed http: $(wget -T 20 -S -q -O /dev/null http://{{PublicHost}}/ 2>&1 | awk '/^  HTTP\//{print $2}' | tail -n 1)"
            echo "vendor https: $(tunnel api.anthropic.com)"
            echo "vendor http: $(wget -T 20 -S -q -O /dev/null http://api.anthropic.com/ 2>&1 | awk '/^  HTTP\//{print $2}' | tail -n 1)"
            echo finished
            """;
        var recording = new RecordingInbox();
        var watcher = new StepWatcher(engine.Docker, engine.RuntimeOptions, recording, new NullOutputStore(), await Masker(), TimeProvider.System);
        var runtime = Runtimes.Of(engine.Docker, engine.RuntimeOptions, watcher);

        var started = await runtime.StartStepAsync(Request(taskId, stepRunId, image, script, [PublicHost]), "task:e5:container:0", Cancel);
        var (exit, output) = await RunToEndAsync(started.ContainerId);
        await watcher.WhenIdleAsync().WaitAsync(TimeSpan.FromMinutes(2), Cancel);

        Assert.Equal(0, exit);
        Assert.Contains("allowed https: HTTP/1.1 200 Connection established", output, StringComparison.Ordinal);
        Assert.Contains("allowed http: 200", output, StringComparison.Ordinal);
        Assert.Contains("vendor https: HTTP/1.1 403 Forbidden", output, StringComparison.Ordinal);
        Assert.Contains("vendor http: 403", output, StringComparison.Ordinal);
        var signal = Assert.Single(recording.Appended);
        Assert.Equal(["api.anthropic.com"], signal.RefusedHosts!);
        // Everything of the step run is gone: the container, the proxy, and the network.
        await Assert.ThrowsAnyAsync<Docker.DotNet.DockerApiException>(() => engine.Docker.Containers.InspectContainerAsync($"hephaestus-egress-{stepRunId}", Cancel));
        await Assert.ThrowsAnyAsync<Docker.DotNet.DockerApiException>(() => engine.Docker.Networks.InspectNetworkAsync($"hephaestus-net-{stepRunId}", Cancel));
        await watcher.DisposeAsync();
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task With_no_list_a_public_host_is_reached_and_git_is_told_to_fetch_github_over_https()
    {
        var (image, taskId) = await PrepareAsync();
        var stepRunId = Guid.CreateVersion7();
        var script = $$"""
            echo "public https: $(printf 'CONNECT {{PublicHost}}:443 HTTP/1.1\r\n\r\n' | nc -w 20 hephaestus-egress 3128 | head -n 1 | tr -d '\r')"
            echo "public http: $(wget -T 20 -S -q -O /dev/null http://{{PublicHost}}/ 2>&1 | awk '/^  HTTP\//{print $2}' | tail -n 1)"
            echo "git: $GIT_CONFIG_COUNT $GIT_CONFIG_KEY_0=$GIT_CONFIG_VALUE_0 $GIT_CONFIG_KEY_1=$GIT_CONFIG_VALUE_1"
            env | grep -iE 'java|maven|gradle|npm_config|pip_|goproxy|cargo|nuget' || echo "nothing ecosystem-specific"
            echo finished
            """;

        var started = await engine.Runtime.StartStepAsync(Request(taskId, stepRunId, image, script, null), "task:e6:container:0", Cancel);
        var (exit, output) = await RunToEndAsync(started.ContainerId);

        Assert.Equal(0, exit);
        Assert.Contains("public https: HTTP/1.1 200 Connection established", output, StringComparison.Ordinal);
        Assert.Contains("public http: 200", output, StringComparison.Ordinal);
        Assert.Contains("git: 2 url.https://github.com/.insteadOf=git@github.com: url.https://github.com/.insteadOf=ssh://git@github.com/", output, StringComparison.Ordinal);
        Assert.Contains("nothing ecosystem-specific", output, StringComparison.Ordinal);
        Assert.DoesNotContain("refused", await ProxyLogAsync(stepRunId), StringComparison.Ordinal);
    }

    private static async Task<Hephaestus.Core.Secrets.SecretMasker> Masker()
    {
        var store = Substitute.For<Hephaestus.Core.Ports.ISecretStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        var masker = new Hephaestus.Core.Secrets.SecretMasker(store);
        await masker.RebuildAsync(Cancel);
        return masker;
    }

    /// <summary>The address at which a container on Docker's default network reaches this machine, which a step container must not.</summary>
    private async Task<string> HostAddressAsync(ResolvedImage image)
    {
        var created = await engine.Docker.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Name = $"hephaestus-control-{Guid.NewGuid():N}",
                Image = image.Reference,
                Labels = new Dictionary<string, string> { ["hephaestus.installation"] = engine.InstallationId },
                Entrypoint = ["/bin/sh"],
                Cmd = ["-c", "ip route | awk '/default/ {print $3}'"],
            },
            Cancel);
        await engine.Docker.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), Cancel);
        var (_, output) = await RunToEndAsync(created.ID);
        var address = output.Trim();
        Unless(IPAddress.TryParse(address, out _), "The default network of Docker names no gateway on this machine.");
        return address;
    }

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

    private sealed class RecordingInbox : Hephaestus.Core.Ports.ISignalInbox
    {
        public List<Hephaestus.Core.Signals.Signal> Appended { get; } = [];

        public Task<int> AppendAsync(IReadOnlyList<Hephaestus.Core.Signals.Signal> signals, CancellationToken cancellationToken)
        {
            lock (Appended)
            {
                Appended.AddRange(signals);
            }

            return Task.FromResult(signals.Count);
        }

        public Task<IReadOnlyList<Hephaestus.Core.Signals.StoredSignal>> ReadAsync(long afterSequence, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Hephaestus.Core.Signals.StoredSignal>> ReadPendingAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NullOutputStore : Hephaestus.Core.Ports.IOutputStore
    {
        public Task<Hephaestus.Core.Ports.IOutputWriter> BeginAsync(Hephaestus.Core.Output.OutputRun run, CancellationToken cancellationToken) => Task.FromResult<Hephaestus.Core.Ports.IOutputWriter>(new Writer(run));

        public Task<Hephaestus.Core.Output.StoredOutput?> FindAsync(Guid stepRunId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(Guid stepRunId, CancellationToken cancellationToken) => throw new NotSupportedException();

        private sealed class Writer(Hephaestus.Core.Output.OutputRun run) : Hephaestus.Core.Ports.IOutputWriter
        {
            public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) => ValueTask.CompletedTask;

            public Task<Hephaestus.Core.Output.StoredOutput> CompleteAsync(CancellationToken cancellationToken) => Task.FromResult(new Hephaestus.Core.Output.StoredOutput(run.TaskId, run.StepRunId, run.ContainerId, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
