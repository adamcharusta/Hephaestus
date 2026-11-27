using System.Diagnostics;
using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Workspaces;
using Hephaestus.Workspaces.Egress;
using Message = Hephaestus.Core.Localization.Message;

namespace Hephaestus.Workspaces;

public sealed partial class DockerWorkspaceRuntime
{
    private const long ProxyMemoryBytes = 512L * 1024 * 1024;
    private const string ProxyTemporaryDirectory = "/tmp";

    /// <summary>
    /// The definition of the egress proxy of a step run (AD-7, AR-41): the <c>hephaestus</c> image's <c>egress-proxy</c> command, as the step user, with every capability dropped,
    /// <c>no-new-privileges</c>, a read-only root file system, no mount but a small memory-only <c>/tmp</c>, no credential, no Docker socket, no published port, and limits of its own. Its only
    /// configuration is the step run's list of allowed hosts. It is created on the network with a route out, and joins the step run's internal network, as the one thing on it, before it starts.
    /// </summary>
    private CreateContainerParameters EgressProxyContainer(StepContainerRequest request, string idempotencyKey)
    {
        var environment = new List<string> { "DOTNET_EnableDiagnostics=0", "DOTNET_TieredPGO=0" };
        if (EgressAllowlist.Serialize(request.AllowedEgressHosts) is { } hosts)
        {
            environment.Add($"{EgressAllowlist.VariableName}={hosts}");
        }

        return new CreateContainerParameters
        {
            Name = WorkspaceLabels.EgressProxy(request.StepRunId),
            Image = options.EgressProxyImage,
            User = StepContainerLayout.User,
            Entrypoint = ["hephaestus", "egress-proxy"],
            Env = environment,
            Labels = WorkspaceLabels.For(options.InstallationId, TaskLabel(request), request.StepRunId, idempotencyKey, WorkspaceLabels.EgressProxyRole),
            HostConfig = new HostConfig
            {
                CapDrop = ["ALL"],
                SecurityOpt = ["no-new-privileges"],
                Privileged = false,
                PublishAllPorts = false,
                ReadonlyRootfs = true,
                PidsLimit = StepContainerLayout.ProcessLimit,
                Memory = ProxyMemoryBytes,
                MemorySwap = ProxyMemoryBytes,
                NanoCPUs = 1_000_000_000L,
                NetworkMode = options.OutboundNetwork,
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.No },
                Tmpfs = new Dictionary<string, string> { [ProxyTemporaryDirectory] = $"rw,nosuid,nodev,size=16m,uid={StepContainerLayout.UserId},gid={StepContainerLayout.GroupId},mode=0700" },
                // The runtime reads the proxy's log to learn what it refused, and a log that cannot grow without bound cannot be filled by a step.
                LogConfig = new LogConfig { Type = "json-file", Config = new Dictionary<string, string> { ["max-size"] = "4m", ["max-file"] = "1" } },
            },
        };
    }

    /// <summary>
    /// Creates the step run's egress proxy, puts it on the step run's network under the fixed name, starts it, and waits until it says it is listening. The proxy holds no state, so one that
    /// a crash left is removed and made again: the step container, which is made after this returns, is not there if this runs.
    /// </summary>
    private async Task StartEgressProxyAsync(StepContainerRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        var definition = EgressProxyContainer(request, idempotencyKey);
        await RemoveIfExistsAsync(WorkspaceLabels.EgressProxy(request.StepRunId), cancellationToken);
        string id;
        try
        {
            id = (await docker.Containers.CreateContainerAsync(definition, cancellationToken)).ID;
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // The image is not on this machine. A step that runs without its proxy would run with no way out, or, worse, with one that was not checked.
            throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceEgressProxyNotReady, options.EgressProxyImage), exception);
        }

        try
        {
            await docker.Networks.ConnectNetworkAsync(
                WorkspaceLabels.StepNetwork(request.StepRunId),
                new NetworkConnectParameters { Container = id, EndpointConfig = new EndpointSettings { Aliases = [EgressProxyAddress.Host] } },
                cancellationToken);
            await docker.Containers.StartContainerAsync(id, new ContainerStartParameters(), cancellationToken);
            await WaitUntilListeningAsync(id, cancellationToken);
        }
        catch
        {
            await RemoveQuietlyAsync(id);
            throw;
        }
    }

    /// <summary>Whether the step run's proxy exists and runs; a found step container that has not started yet needs one before it does.</summary>
    private async Task<bool> EgressProxyRunsAsync(Guid stepRunId, CancellationToken cancellationToken)
    {
        try
        {
            return (await docker.Containers.InspectContainerAsync(WorkspaceLabels.EgressProxy(stepRunId), cancellationToken)).State?.Running == true;
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>Waits for the proxy's line that says it is listening; a proxy that has stopped, or does not say so in time, stops the step from being started.</summary>
    private async Task WaitUntilListeningAsync(string container, CancellationToken cancellationToken)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            // Listening and still running: a proxy that said so and then ended is of no use to the step.
            var state = (await docker.Containers.InspectContainerAsync(container, cancellationToken)).State;
            var running = state?.Running == true;
            if (running && EgressRefusals.IsReady(await ContainerLogs.ReadAsync(docker, container, cancellationToken)))
            {
                return;
            }

            if (!running || waited.Elapsed >= options.EgressProxyReadyLimit)
            {
                throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceEgressProxyNotReady, options.EgressProxyImage));
            }

            await Task.Delay(options.EgressProxyPollInterval, cancellationToken);
        }
    }
}
