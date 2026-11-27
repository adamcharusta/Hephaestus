using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Secrets;
using Hephaestus.Host.Hosting;
using Docker.DotNet;
using Docker.DotNet.Models;
using Hephaestus.Workspaces;
using NSubstitute;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Host.Tests.Hosting;

/// <summary>Story 3.2, AD-7: the host holds the one Docker runtime, built from the lock the image carries and the installation's id.</summary>
public class WorkspaceRuntimeSetupTests
{
    [Fact]
    public void The_runtime_is_built_from_the_lock_the_image_carries()
    {
        using var installation = new TempSocket();
        installation.PrepareAgents();

        using var client = WorkspaceRuntimeSetup.CreateClient();
        var secrets = Substitute.For<ISecretStore>();

        var runtime = WorkspaceRuntimeSetup.Create(client, WorkspaceRuntimeSetup.CreateOptions(installation.Settings()), new NoWatcher(), secrets, new SecretMasker(secrets));

        Assert.IsType<DockerWorkspaceRuntime>(runtime);
    }

    [Fact]
    public void The_options_carry_the_egress_proxy_image_the_settings_name()
    {
        using var installation = new TempSocket();
        installation.PrepareAgents();

        var options = WorkspaceRuntimeSetup.CreateOptions(installation.Settings() with { EgressProxyImage = "hephaestus:dev" });

        Assert.Equal("hephaestus:dev", options.EgressProxyImage);
    }

    [Fact]
    public void The_options_carry_the_tools_image_and_the_bundles_directory_the_settings_name()
    {
        using var installation = new TempSocket();
        installation.PrepareAgents();

        var options = WorkspaceRuntimeSetup.CreateOptions(installation.Settings() with { ToolsImage = "hephaestus-tools:dev" });

        Assert.Equal("hephaestus-tools:dev", options.ToolsImage);
        Assert.Equal(installation.Settings().BundlesDirectory, options.BundlesDirectory);
    }

    [Fact]
    public async Task The_host_offers_the_mirrors_as_the_one_port_of_the_gateway()
    {
        using var installation = new TempSocket();
        installation.PrepareAgents();
        await using var host = await RunningHost.StartAsync(installation: installation);

        var first = host.App.Services.GetRequiredService<IRepositoryMirrors>();

        Assert.IsType<Hephaestus.GitHub.GitHubMirrors>(first);
        Assert.Same(first, host.App.Services.GetRequiredService<IRepositoryMirrors>());
    }

    [Fact]
    public async Task The_host_offers_the_same_gateway_as_the_port_that_pushes_so_that_a_fetch_and_a_push_share_one_gate_and_the_loop_has_it()
    {
        using var installation = new TempSocket();
        installation.PrepareAgents();
        await using var host = await RunningHost.StartAsync(installation: installation);

        var mirrors = host.App.Services.GetRequiredService<IRepositoryMirrors>();
        var branches = host.App.Services.GetRequiredService<ITaskBranches>();

        Assert.Same(mirrors, branches);
        Assert.Same(branches, host.App.Services.GetRequiredService<ITaskBranches>());
        // The loop is built by the container and gets the port; without it every push intent would be refused.
        var loop = host.App.Services.GetRequiredService<Hephaestus.Core.Loop.OrchestratorLoop>();
        var held = typeof(Hephaestus.Core.Loop.OrchestratorLoop)
            .GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Single(field => field.FieldType == typeof(ITaskBranches))
            .GetValue(loop);
        Assert.Same(branches, held);
    }

    [Fact]
    public void A_lock_that_cannot_be_parsed_stops_the_build_of_the_runtime()
    {
        using var installation = new TempSocket();
        File.WriteAllText(installation.AgentsLockFile, "not a lock");

        Assert.Throws<AgentLockException>(() => WorkspaceRuntimeSetup.CreateOptions(installation.Settings()));
    }

    [Fact]
    public async Task The_host_offers_the_runtime_as_the_one_port()
    {
        using var installation = new TempSocket();
        installation.PrepareAgents();
        await using var host = await RunningHost.StartAsync(installation: installation);

        var first = host.App.Services.GetRequiredService<IWorkspaceRuntime>();

        Assert.IsType<DockerWorkspaceRuntime>(first);
        Assert.Same(first, host.App.Services.GetRequiredService<IWorkspaceRuntime>());
    }

    [Fact]
    public async Task The_host_holds_one_watcher_that_the_runtime_is_given()
    {
        using var installation = new TempSocket();
        installation.PrepareAgents();
        await using var host = await RunningHost.StartAsync(installation: installation);

        var watcher = host.App.Services.GetRequiredService<IStepWatcher>();

        Assert.IsType<StepWatcher>(watcher);
        Assert.Same(watcher, host.App.Services.GetRequiredService<StepWatcher>());
        Assert.NotNull(host.App.Services.GetRequiredService<IOutputStore>());
    }

    [Fact]
    public async Task Stopping_the_host_stops_the_watcher_so_that_no_container_is_looked_at_after_it_and_the_container_is_left_alone()
    {
        using var installation = new TempSocket();
        var vendor = installation.PrepareAgents();
        var looks = 0;
        var removed = 0;
        var docker = Substitute.For<IDockerClient>();
        docker.Containers.InspectContainerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Interlocked.Increment(ref looks);
            return Task.FromResult(new ContainerInspectResponse { State = new() { Status = "running" } });
        });
        docker.Containers.GetContainerLogsAsync(Arg.Any<string>(), Arg.Any<ContainerLogsParameters>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromException<MultiplexedStream>(new HttpRequestException("no log")));
        docker.Containers.RemoveContainerAsync(Arg.Any<string>(), Arg.Any<ContainerRemoveParameters>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Interlocked.Increment(ref removed);
            return Task.CompletedTask;
        });
        var options = new WorkspaceRuntimeOptions("hephaestus", vendor.Lock, AgentArchitecture.X64) { StepPollInterval = TimeSpan.FromMilliseconds(10) };
        var host = await RunningHost.StartAsync(installation: installation, configure: builder =>
        {
            builder.Services.AddSingleton(docker);
            builder.Services.AddSingleton(options);
        });

        host.App.Services.GetRequiredService<IStepWatcher>().Watch(new WatchedStep(Guid.CreateVersion7(), Guid.CreateVersion7(), "container-1", DateTimeOffset.UtcNow.AddHours(1)));
        while (Volatile.Read(ref looks) < 3)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        await host.DisposeAsync();
        var after = Volatile.Read(ref looks);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(after, Volatile.Read(ref looks));
        Assert.Equal(0, removed);
    }

    private sealed class NoWatcher : IStepWatcher
    {
        public void Watch(WatchedStep step)
        {
        }
    }
}
