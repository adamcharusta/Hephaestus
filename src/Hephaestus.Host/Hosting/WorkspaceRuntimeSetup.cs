using System.Runtime.InteropServices;
using Docker.DotNet;
using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Secrets;
using Hephaestus.Workspaces;

namespace Hephaestus.Host.Hosting;

/// <summary>
/// Builds the Docker runtime (AD-7): the client of the socket mounted into this container, which no other container has, the lock that says
/// which Agent builds a step container receives, and the watcher that sees each step container to its end (AD-18). Nothing connects until the loop
/// asks the runtime for something.
/// </summary>
internal static class WorkspaceRuntimeSetup
{
    /// <summary>How long one request to Docker may take, besides a pull, whose stream has no limit of its own: a hung call must not hold the loop for long (AD-18).</summary>
    private static readonly TimeSpan RequestLimit = TimeSpan.FromMinutes(5);

    public static IDockerClient CreateClient() => new DockerClientBuilder().WithTimeout(RequestLimit).Build();

    public static WorkspaceRuntimeOptions CreateOptions(HostSettings settings)
    {
        var agentLock = AgentLock.Parse(File.ReadAllText(settings.AgentsLockFile));
        var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? AgentArchitecture.Arm64 : AgentArchitecture.X64;
        return new WorkspaceRuntimeOptions(settings.InstallationId, agentLock, architecture) { EgressProxyImage = settings.EgressProxyImage, ToolsImage = settings.ToolsImage, BundlesDirectory = settings.BundlesDirectory };
    }

    /// <summary>The watcher tells the host's logger what fails while it sees a step to its end; the sentences it passes name a step run and hold none of its output.</summary>
    public static StepWatcher CreateWatcher(IServiceProvider services) => new(
        services.GetRequiredService<IDockerClient>(),
        services.GetRequiredService<WorkspaceRuntimeOptions>(),
        services.GetRequiredService<ISignalInbox>(),
        services.GetRequiredService<IOutputStore>(),
        services.GetRequiredService<SecretMasker>(),
        services.GetRequiredService<TimeProvider>(),
        (problem, exception) => services.GetRequiredService<ILoggerFactory>().CreateLogger("Hephaestus.Workspaces").LogWarning(exception, "{Problem}", problem));

    public static IWorkspaceRuntime Create(IDockerClient docker, WorkspaceRuntimeOptions options, IStepWatcher watcher, ISecretStore secrets, SecretMasker masker) => new DockerWorkspaceRuntime(docker, options, watcher, secrets, masker);
}
