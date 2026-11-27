using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Invocations;
using SecretReference = Hephaestus.Core.Invocations.SecretReference;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Readiness;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces.Tests;

/// <summary>Story 3.10, AD-20, AD-18, AD-7: a probe runs in the fixed image with no Task volume, through an egress proxy limited to the hosts of the invocation, and leaves nothing behind.</summary>
public class ProbeContainerTests
{
    private const string Token = "sk-ant-oat01-the-token-of-the-owner";
    private static readonly Guid ProbeId = Guid.Parse("018f4b6e-0000-7000-8000-000000000009");
    private static readonly string Digest = "sha256:" + new string('e', 64);

    private readonly DockerDouble _docker = new();
    private readonly FakeVendor _vendor = new();
    private readonly RecordingWatcher _watcher = new();
    private readonly TestSecrets _secrets = new TestSecrets().With("claude-code-oauth-token", Token);

    public ProbeContainerTests()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:probe", "amd64", $"docker.io/library/debian@{Digest}");
        _docker.ImagePaths.Add("/lib64/ld-linux-x86-64.so.2");
        _docker.Inspector = (id, _) => Task.FromResult(id.Contains("agent-probe", StringComparison.Ordinal) ? DockerDouble.Inspected("exited", 0) : DockerDouble.Inspected("running"));
        _docker.Logs = (_, _) => Task.FromResult(DockerDouble.Output((1, "{\"type\":\"result\"}\n")));
    }

    private WorkspaceRuntimeOptions Options() => new("test-installation", _vendor.Lock, AgentArchitecture.X64)
    {
        CheckoutPollInterval = TimeSpan.FromMilliseconds(5),
        EgressProxyPollInterval = TimeSpan.FromMilliseconds(5),
    };

    private DockerWorkspaceRuntime Runtime(WorkspaceRuntimeOptions? options = null) => Runtimes.Of(_docker.Client, options ?? Options(), _watcher, _secrets);

    private static ProbeRequest Request(TimeSpan? limit = null, IReadOnlyList<SecretReference>? references = null) => new(
        ProbeId,
        new AgentInvocation(
            ["/hephaestus/agents/claude-code/claude", "-p", "Reply with exactly one word: pong"],
            new Dictionary<string, string> { ["DISABLE_AUTOUPDATER"] = "1" },
            references ?? [new SecretReference("claude-code-oauth-token", "CLAUDE_CODE_OAUTH_TOKEN")],
            [],
            ["api.anthropic.com"],
            ResultLocation.InOutputStream),
        new StepLimits(1000, 2L * 1024 * 1024 * 1024),
        limit ?? TimeSpan.FromSeconds(30));

    private CreateContainerParameters Probe() => _docker.Created.Single(created => created.Name == $"hephaestus-agent-probe-{ProbeId}");

    [Fact]
    public async Task The_probe_runs_in_the_fixed_image_resolved_to_its_digest()
    {
        await Runtime().RunProbeAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Contains($"inspect image {ProbeImage.Reference}", _docker.Calls);
        Assert.Equal($"docker.io/library/debian@{Digest}", Probe().Image);
    }

    [Fact]
    public async Task The_probe_container_has_the_hardening_of_a_step_container_and_the_labels_of_a_probe_that_names_no_Task()
    {
        await Runtime().RunProbeAsync(Request(), TestContext.Current.CancellationToken);
        var container = Probe();
        var host = container.HostConfig!;

        Assert.Equal("10001:10001", container.User);
        Assert.Equal(["ALL"], host.CapDrop!);
        Assert.Equal(["no-new-privileges"], host.SecurityOpt!);
        Assert.False(host.Privileged);
        Assert.Equal($"hephaestus-net-{ProbeId}", host.NetworkMode);
        Assert.Equal("test-installation", container.Labels!["hephaestus.installation"]);
        Assert.Equal(string.Empty, container.Labels["hephaestus.task"]);
        Assert.Equal(string.Empty, container.Labels["hephaestus.step-run"]);
        Assert.Equal("agent-probe", container.Labels["hephaestus.role"]);
        Assert.Equal(["/hephaestus/agents/claude-code/claude"], container.Entrypoint!);
    }

    [Fact]
    public async Task The_probe_has_no_Task_volume_no_context_and_no_Methodology_and_creates_no_volume()
    {
        await Runtime().RunProbeAsync(Request(), TestContext.Current.CancellationToken);

        var mounts = Probe().HostConfig!.Mounts!;
        Assert.Equal(["/hephaestus/agents/certs", "/hephaestus/agents/claude-code", "/hephaestus/agents/codex-cli"], mounts.Select(mount => mount.Target!).Order(StringComparer.Ordinal));
        Assert.All(mounts, mount => Assert.Equal("hephaestus-agents", mount.Source));
        Assert.All(mounts, mount => Assert.True(mount.ReadOnly));
        Assert.Empty(_docker.Volumes);
        Assert.DoesNotContain(_docker.Calls, call => call.Contains("hephaestus-ws-", StringComparison.Ordinal) || call.Contains("context", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_credential_is_resolved_once_when_the_probe_starts_and_is_in_its_environment_only()
    {
        await _secrets.ReadyAsync();

        await Runtime().RunProbeAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(["claude-code-oauth-token"], _secrets.Reads);
        Assert.Contains($"CLAUDE_CODE_OAUTH_TOKEN={Token}", Probe().Env!);
        foreach (var created in _docker.Created)
        {
            Assert.DoesNotContain(Token, JsonSerializer.Serialize(new { created.Name, created.Labels, created.HostConfig?.Mounts, created.Cmd }), StringComparison.Ordinal);
        }

        Assert.DoesNotContain(_docker.Created.Where(created => created.Name != $"hephaestus-agent-probe-{ProbeId}"), created => (created.Env ?? []).Any(entry => entry.Contains(Token, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_proxy_of_the_probe_allows_the_hosts_of_the_invocation_and_no_others()
    {
        await Runtime().RunProbeAsync(Request(), TestContext.Current.CancellationToken);

        var proxy = _docker.Created.Single(created => created.Name == $"hephaestus-egress-{ProbeId}");
        Assert.Contains("HEPHAESTUS_EGRESS_ALLOWED_HOSTS=api.anthropic.com", proxy.Env!);
        Assert.Equal(string.Empty, proxy.Labels!["hephaestus.task"]);
        Assert.Contains($"hephaestus-net-{ProbeId}", _docker.Networks.Select(network => network.Name));
        Assert.True(_docker.Networks.Single(network => network.Name == $"hephaestus-net-{ProbeId}").Internal);
        Assert.All(_docker.Networks, network => Assert.Equal(string.Empty, network.Labels!["hephaestus.task"]));
    }

    [Fact]
    public async Task What_the_probe_printed_comes_back_with_how_it_ended_and_is_not_handed_to_a_watcher_or_a_signal()
    {
        _docker.Inspector = (id, _) => Task.FromResult(id.Contains("agent-probe", StringComparison.Ordinal) ? DockerDouble.Inspected("exited", 3) : DockerDouble.Inspected("running"));
        _docker.Logs = (_, _) => Task.FromResult(DockerDouble.Output((1, "first\r\nsecond\n\n"), (2, "third\n")));

        var result = await Runtime().RunProbeAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(StepExitCause.Exited, result.Cause);
        Assert.Equal(["first", "second", "third"], result.Lines);
        Assert.Empty(_watcher.Watched);
    }

    [Fact]
    public async Task What_the_probe_printed_is_masked_before_it_leaves_the_runtime()
    {
        await _secrets.ReadyAsync();
        _docker.Logs = (_, _) => Task.FromResult(DockerDouble.Output((1, $"Invalid key {Token} given\n")));

        var result = await Runtime().RunProbeAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(["Invalid key *** given"], result.Lines);
    }

    [Fact]
    public async Task Only_the_end_of_a_long_output_is_kept_and_a_line_that_was_cut_is_dropped()
    {
        _docker.Logs = (_, _) => Task.FromResult(DockerDouble.Output((1, "aaaaaaaaaa\nbbbbbbbbbb\ncccccccccc\n")));
        var runtime = Runtime(new WorkspaceRuntimeOptions("test-installation", _vendor.Lock, AgentArchitecture.X64) { CheckoutPollInterval = TimeSpan.FromMilliseconds(5), EgressProxyPollInterval = TimeSpan.FromMilliseconds(5), ProbeOutputLimit = 15 });

        var result = await runtime.RunProbeAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(["cccccccccc"], result.Lines);
    }

    [Theory]
    [InlineData(true, StepExitCause.OutOfMemory)]
    [InlineData(false, StepExitCause.Exited)]
    public async Task A_probe_the_kernel_ended_for_memory_says_so(bool oomKilled, StepExitCause cause)
    {
        _docker.Inspector = (id, _) => Task.FromResult(id.Contains("agent-probe", StringComparison.Ordinal) ? DockerDouble.Inspected("exited", 137, oomKilled) : DockerDouble.Inspected("running"));

        var result = await Runtime().RunProbeAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(cause, result.Cause);
    }

    [Fact]
    public async Task A_container_that_is_dead_when_looked_at_is_lost()
    {
        _docker.Inspector = (id, _) => Task.FromResult(id.Contains("agent-probe", StringComparison.Ordinal) ? DockerDouble.Inspected("dead") : DockerDouble.Inspected("running"));

        var result = await Runtime().RunProbeAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(StepExitCause.Lost, result.Cause);
    }

    [Fact]
    public async Task A_probe_that_runs_past_its_limit_is_removed_by_force_and_reported_as_timed_out()
    {
        _docker.Inspector = (_, _) => Task.FromResult(DockerDouble.Inspected("running"));

        var result = await Runtime().RunProbeAsync(Request(limit: TimeSpan.FromMilliseconds(40)), TestContext.Current.CancellationToken);

        Assert.Equal(StepExitCause.Timeout, result.Cause);
        Assert.Empty(result.Lines);
        Assert.Contains($"id-hephaestus-agent-probe-{ProbeId}", _docker.Removed);
        Assert.True(_docker.Removals.All(removal => removal.Force == true));
        Assert.Empty(_docker.Stops);
    }

    [Fact]
    public async Task Everything_the_probe_made_is_removed_when_it_ends()
    {
        await Runtime().RunProbeAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Contains($"id-hephaestus-agent-probe-{ProbeId}", _docker.Removed);
        Assert.Contains($"hephaestus-egress-{ProbeId}", _docker.Removed);
        Assert.Contains($"hephaestus-net-{ProbeId}", _docker.RemovedNetworks);
    }

    [Fact]
    public async Task Everything_the_probe_made_is_removed_when_it_cannot_start()
    {
        _docker.StartFailsFor = id => id.Contains("agent-probe", StringComparison.Ordinal) ? new InvalidOperationException("no start") : null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Runtime().RunProbeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Contains($"id-hephaestus-agent-probe-{ProbeId}", _docker.Removed);
        Assert.Contains($"hephaestus-egress-{ProbeId}", _docker.Removed);
        Assert.Contains($"hephaestus-net-{ProbeId}", _docker.RemovedNetworks);
    }

    [Fact]
    public async Task A_container_that_Docker_cannot_start_is_a_probe_that_could_not_run_and_is_cleaned_up_like_any_other()
    {
        _docker.StartFailsFor = id => id.Contains("agent-probe", StringComparison.Ordinal) ? new DockerApiException(System.Net.HttpStatusCode.InternalServerError, "no start") : null;

        var refusal = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => Runtime().RunProbeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(MessageIds.WorkspaceProbeDocker, refusal.Info.Id);
        Assert.Contains("no start", (string)refusal.Info.Arguments[0], StringComparison.Ordinal);
        Assert.Contains($"id-hephaestus-agent-probe-{ProbeId}", _docker.Removed);
        Assert.Contains($"hephaestus-net-{ProbeId}", _docker.RemovedNetworks);
    }

    [Fact]
    public async Task A_daemon_that_does_not_answer_is_a_probe_that_could_not_run_and_not_a_finding()
    {
        _docker.Inspector = (_, _) => Task.FromException<ContainerInspectResponse>(new HttpRequestException("connection refused"));

        var refusal = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => Runtime().RunProbeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(MessageIds.WorkspaceProbeDocker, refusal.Info.Id);
        Assert.Contains("connection refused", (string)refusal.Info.Arguments[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cancelled_probe_is_cancelled_and_not_reported_as_a_fault_of_Docker()
    {
        using var cancel = new CancellationTokenSource();
        _docker.Inspector = (_, _) =>
        {
            cancel.Cancel();
            throw new OperationCanceledException(cancel.Token);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runtime().RunProbeAsync(Request(), cancel.Token));

        Assert.Contains($"hephaestus-net-{ProbeId}", _docker.RemovedNetworks);
    }

    [Fact]
    public async Task A_failure_to_remove_what_the_probe_made_does_not_hide_what_it_found()
    {
        _docker.NetworkRemoveFails = _ => new HttpRequestException("gone");
        _docker.RemoveFails = id => id.StartsWith("id-", StringComparison.Ordinal) ? new HttpRequestException("gone") : null;

        var result = await Runtime().RunProbeAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(StepExitCause.Exited, result.Cause);
    }

    [Fact]
    public async Task A_credential_that_is_not_stored_refuses_the_probe_before_anything_is_made()
    {
        var refusal = await Assert.ThrowsAsync<WorkspaceRuntimeException>(() => Runtime().RunProbeAsync(Request(references: [new SecretReference("anthropic-api-key", "ANTHROPIC_API_KEY")]), TestContext.Current.CancellationToken));

        Assert.Equal(MessageIds.WorkspaceSecretMissing, refusal.Info.Id);
        Assert.DoesNotContain(_docker.Created, created => created.Labels!["hephaestus.role"] is "agent-probe" or "egress-proxy");
        Assert.Empty(_docker.Networks);
    }

    [Fact]
    public async Task A_probe_image_that_cannot_be_fetched_is_refused_with_the_image_named()
    {
        _docker.Image = null;
        _docker.PullFails = new HttpRequestException("offline");

        var refusal = await Assert.ThrowsAsync<ImageNotUsableException>(() => Runtime().RunProbeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(MessageIds.WorkspaceImageUnavailable, refusal.Info.Id);
        Assert.Equal(ProbeImage.Reference, refusal.Info.Arguments[0]);
        Assert.Empty(_docker.Created);
    }

    [Fact]
    public async Task A_probe_needs_a_limit_above_zero()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Runtime().RunProbeAsync(Request(limit: TimeSpan.Zero), TestContext.Current.CancellationToken));

        Assert.Empty(_docker.Created);
    }

    [Fact]
    public void The_probe_image_the_options_carry_is_the_one_the_release_fixes()
    {
        Assert.Equal(ProbeImage.Reference, Options().ProbeImage);
    }
}
