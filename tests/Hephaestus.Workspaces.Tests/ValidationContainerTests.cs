using System.Security.Cryptography;
using Docker.DotNet.Models;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Validation;
using Hephaestus.Core.Workspaces;

namespace Hephaestus.Workspaces.Tests;

/// <summary>
/// Story 3.12, AD-6, AD-7, AD-13, FR-31: what the runtime asks Docker for when it runs a Validation command, checked against an Engine in memory. A fresh checkout of the pushed tip comes first; the container is the
/// Task image's, runs the command through bash, mounts the Task volume and nothing else of a step, holds no credential, and is watched like any step.
/// </summary>
public sealed class ValidationContainerTests : IDisposable
{
    private static readonly Guid TaskId = Guid.Parse("018f4b6e-0000-7000-8000-000000000001");
    private static readonly Guid StepRunId = Guid.Parse("018f4b6e-0000-7000-8000-000000000002");
    private const string Branch = "hephaestus/12-let-the-project-decide";
    private const string Key = "task:s2:validation:0";
    private static readonly DateTimeOffset Deadline = new(2026, 11, 28, 13, 0, 0, TimeSpan.Zero);
    private static readonly ResolvedImage Image = new("registry.test/app@sha256:" + new string('a', 64), "sha256:" + new string('a', 64), AgentArchitecture.X64, CLibrary.Glibc);

    private readonly string _bundles = Directory.CreateTempSubdirectory("hephaestus-bundles-").FullName;
    private readonly DockerDouble _docker = new();
    private readonly RecordingWatcher _watcher = new();
    private readonly byte[] _content = [.. "a bundle of a repository"u8];

    public ValidationContainerTests()
    {
        _docker.Image = DockerDouble.ImageOf("sha256:" + new string('c', 64), "amd64");

        // The checkout ends at once; the proxy runs; the command's container is not looked at, for the watcher sees it to its end.
        _docker.Inspector = (id, _) => Task.FromResult(id.StartsWith("id-hephaestus-checkout-", StringComparison.Ordinal) ? DockerDouble.Inspected("exited", 0) : DockerDouble.Inspected("running"));
    }

    public void Dispose()
    {
        Directory.Delete(_bundles, recursive: true);
        GC.SuppressFinalize(this);
    }

    private DockerWorkspaceRuntime Runtime() => Runtimes.Of(
        _docker.Client,
        new WorkspaceRuntimeOptions("test-installation", new FakeVendor().Lock, AgentArchitecture.X64)
        {
            ToolsImage = "registry.test/hephaestus-tools:test",
            BundlesDirectory = _bundles,
            CheckoutPollInterval = TimeSpan.FromMilliseconds(5),
            EgressProxyPollInterval = TimeSpan.FromMilliseconds(5),
        },
        _watcher);

    private BundleReference WriteBundle()
    {
        const string name = "task/tip.bundle";
        var path = Path.Combine(_bundles, "task", "tip.bundle");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, _content);
        return new BundleReference(name, Convert.ToHexStringLower(SHA256.HashData(_content)), new string('d', 40));
    }

    private ValidationCommandRequest Request(string command = "dotnet test", IReadOnlyList<string>? hosts = null) =>
        new(TaskId, StepRunId, Branch, WriteBundle(), Image, command, new StepLimits(2000, 4L * 1024 * 1024 * 1024), hosts ?? [], Deadline);

    private async Task<CreateContainerParameters> StartedAsync(ValidationCommandRequest? request = null)
    {
        await Runtime().StartValidationAsync(request ?? Request(), Key, TestContext.Current.CancellationToken);
        return _docker.Created.Single(created => created.Name == $"hephaestus-step-{StepRunId}");
    }

    [Fact]
    public async Task The_tree_is_checked_out_afresh_before_the_command_starts()
    {
        await StartedAsync();

        var checkout = _docker.Calls.IndexOf($"create container hephaestus-checkout-{StepRunId}");
        var start = _docker.Calls.IndexOf($"start id-hephaestus-step-{StepRunId}");
        Assert.True(checkout >= 0 && start > checkout, string.Join('\n', _docker.Calls));
        Assert.Contains($"remove id-hephaestus-checkout-{StepRunId}", _docker.Calls);
        Assert.True(_docker.Calls.IndexOf($"remove id-hephaestus-checkout-{StepRunId}") < start);
        Assert.False(File.Exists(Path.Combine(_bundles, "task", "tip.bundle")));
    }

    [Fact]
    public async Task The_command_runs_through_bash_in_the_Task_image_as_the_step_user()
    {
        var container = await StartedAsync(Request("npm test -- --ci"));

        Assert.Equal(Image.Reference, container.Image);
        Assert.Equal(["bash"], container.Entrypoint);
        Assert.Equal(["-c", "npm test -- --ci"], container.Cmd);
        Assert.Equal("10001:10001", container.User);
        Assert.Equal("/workspace", container.WorkingDir);
        Assert.Equal(["ALL"], container.HostConfig!.CapDrop);
        Assert.Equal(["no-new-privileges"], container.HostConfig.SecurityOpt);
        Assert.Equal(512, container.HostConfig.PidsLimit);
        Assert.Equal(2_000_000_000, container.HostConfig.NanoCPUs);
        Assert.Equal(4L * 1024 * 1024 * 1024, container.HostConfig.Memory);
        Assert.Equal("step", container.Labels!["hephaestus.role"]);
        Assert.Equal(StepRunId.ToString(), container.Labels["hephaestus.step-run"]);
    }

    [Fact]
    public async Task The_Task_volume_is_the_only_mount_so_no_context_no_Methodology_and_no_Agent_CLI_is_there()
    {
        var container = await StartedAsync();

        var mount = Assert.Single(container.HostConfig!.Mounts!);
        Assert.Equal($"hephaestus-ws-{TaskId}", mount.Source);
        Assert.Equal("/workspace", mount.Target);
        Assert.False(mount.ReadOnly);
        Assert.True(mount.VolumeOptions!.NoCopy);
        Assert.Equal(["/hephaestus/home"], container.HostConfig.Tmpfs!.Keys);
        Assert.Empty(_docker.Volumes);
        Assert.DoesNotContain(_docker.Calls, call => call.StartsWith("extract ", StringComparison.Ordinal) && call.Contains("context", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_container_holds_no_credential()
    {
        var container = await StartedAsync();

        // Nothing is in the environment but the runtime's own: the proxy and git's rewrite, and the throwaway HOME.
        Assert.All(container.Env!, variable => Assert.True(variable.Split('=')[0] == "HOME" || EgressEnvironment.Variables.ContainsKey(variable.Split('=')[0]), variable));
        Assert.Contains("HOME=/hephaestus/home", container.Env!);
    }

    [Fact]
    public async Task The_egress_proxy_allows_the_Projects_list_alone()
    {
        await StartedAsync(Request(hosts: ["registry.npmjs.org", "api.example.com"]));

        var proxy = _docker.Created.Single(created => created.Name == $"hephaestus-egress-{StepRunId}");
        Assert.Contains(proxy.Env!, variable => variable.Contains("api.example.com,registry.npmjs.org", StringComparison.Ordinal));
        Assert.DoesNotContain(proxy.Env!, variable => variable.Contains("anthropic", StringComparison.OrdinalIgnoreCase) || variable.Contains("openai", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_runtime_alone_sees_the_command_to_its_end_by_the_deadline_of_the_request()
    {
        await StartedAsync();

        var watched = Assert.Single(_watcher.Watched);
        Assert.Equal(StepRunId, watched.StepRunId);
        Assert.Equal(TaskId, watched.TaskId);
        Assert.Equal(Deadline, watched.Deadline);
    }

    [Fact]
    public async Task A_container_that_exists_is_found_again_and_the_tree_is_not_made_again_beneath_it()
    {
        _docker.Existing.Add(new ContainerListResponse { ID = "found", State = "running", Names = [$"/hephaestus-step-{StepRunId}"] });
        var request = Request();

        var started = await Runtime().StartValidationAsync(request, Key, TestContext.Current.CancellationToken);

        Assert.False(started.Created);
        Assert.Equal("found", started.ContainerId);
        Assert.DoesNotContain(_docker.Calls, call => call.Contains("hephaestus-checkout-", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(_bundles, "task", "tip.bundle")));
    }

    [Fact]
    public async Task A_command_that_cannot_be_given_a_container_leaves_the_tree_alone()
    {
        var request = new ValidationCommandRequest(TaskId, StepRunId, Branch, WriteBundle(), Image, "x", new StepLimits(2000, 1024), [], Deadline);

        await Assert.ThrowsAsync<ArgumentException>(() => Runtime().StartValidationAsync(request, Key, TestContext.Current.CancellationToken));

        Assert.Empty(_docker.Created);
    }

    [Fact]
    public async Task A_Validation_container_is_refused_what_a_step_is_given()
    {
        var runtime = Runtime();
        var withContext = Request().Container() with { Context = [new WorkspaceFile("instruction.md", [1])] };
        var withMethodology = Request().Container() with { Methodology = [new WorkspaceFile("manifest.yml", [1])] };
        var withSecret = Request().Container() with { SecretReferences = [new Hephaestus.Core.Invocations.SecretReference("claude-code-oauth-token", "CLAUDE_CODE_OAUTH_TOKEN")] };

        foreach (var container in new[] { withContext, withMethodology, withSecret })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => runtime.StartStepAsync(container, Key, TestContext.Current.CancellationToken));
        }

        Assert.Empty(_docker.Created);
    }
}
