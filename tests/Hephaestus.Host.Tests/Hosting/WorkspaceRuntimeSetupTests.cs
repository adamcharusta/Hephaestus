using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Ports;
using Hephaestus.Host.Hosting;
using Hephaestus.Workspaces;
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

        var runtime = WorkspaceRuntimeSetup.Create(installation.Settings(), client);

        Assert.IsType<DockerWorkspaceRuntime>(runtime);
    }

    [Fact]
    public void A_lock_that_cannot_be_parsed_stops_the_build_of_the_runtime()
    {
        using var installation = new TempSocket();
        File.WriteAllText(installation.AgentsLockFile, "not a lock");

        using var client = WorkspaceRuntimeSetup.CreateClient();

        Assert.Throws<AgentLockException>(() => WorkspaceRuntimeSetup.Create(installation.Settings(), client));
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
}
