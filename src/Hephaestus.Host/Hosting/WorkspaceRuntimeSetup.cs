using System.Runtime.InteropServices;
using Docker.DotNet;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Ports;
using Hephaestus.Workspaces;

namespace Hephaestus.Host.Hosting;

/// <summary>
/// Builds the Docker runtime (AD-7): the client of the socket mounted into this container, which no other container has, and the lock that says
/// which Agent builds a step container receives. Nothing connects until the loop asks the runtime for something.
/// </summary>
internal static class WorkspaceRuntimeSetup
{
    /// <summary>How long one request to Docker may take, besides a pull, whose stream has no limit of its own: a hung call must not hold the loop for long (AD-18).</summary>
    private static readonly TimeSpan RequestLimit = TimeSpan.FromMinutes(5);

    public static IDockerClient CreateClient() => new DockerClientBuilder().WithTimeout(RequestLimit).Build();

    public static IWorkspaceRuntime Create(HostSettings settings, IDockerClient docker)
    {
        var agentLock = AgentLock.Parse(File.ReadAllText(settings.AgentsLockFile));
        var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? AgentArchitecture.Arm64 : AgentArchitecture.X64;
        return new DockerWorkspaceRuntime(docker, new WorkspaceRuntimeOptions(settings.InstallationId, agentLock, architecture));
    }
}
