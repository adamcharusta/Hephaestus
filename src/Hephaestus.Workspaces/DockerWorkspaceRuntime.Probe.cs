using System.Diagnostics;
using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Workspaces;
using Message = Hephaestus.Core.Localization.Message;

namespace Hephaestus.Workspaces;

public sealed partial class DockerWorkspaceRuntime
{
    public async Task<ProbeResult> RunProbeAsync(ProbeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Limit <= TimeSpan.Zero)
        {
            throw new ArgumentException("A probe needs a limit above zero.", nameof(request));
        }

        try
        {
            return await ProbeAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is DockerApiException or HttpRequestException or IOException or TimeoutException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // A daemon that is down or does not answer is the commonest reason a probe cannot run, and it says nothing of the Agent: it is reported as what it is, never as a finding.
            throw new WorkspaceRuntimeException(new Message(MessageIds.WorkspaceProbeDocker, exception.Message), exception);
        }
    }

    private async Task<ProbeResult> ProbeAsync(ProbeRequest request, CancellationToken cancellationToken)
    {
        var invocation = request.Invocation;

        // A probe has no Task, no context, and no Methodology: its container has the Agent mounts and its own proxy, whose list is the hosts of the invocation and no others. The request is the
        // step container's, with the Task left out (Guid.Empty names none) and the probe's id for the step run, so that everything the runtime makes for it is named after the probe.
        var image = await ResolveImageAsync(options.ProbeImage, cancellationToken);
        var hosts = EgressPolicy.AllowedHosts([], invocation.EgressHosts, EgressDefault.AllowlistOnly);
        var key = $"probe:{request.ProbeId:D}";
        var stepRequest = new StepContainerRequest(Guid.Empty, request.ProbeId, image, invocation.Command, invocation.Environment, request.Limits, [], [], DateTimeOffset.MaxValue, hosts, invocation.SecretReferences);
        var definition = StepContainer(stepRequest, key, probe: true);
        AddSecrets(definition, await ResolveSecretsAsync(invocation.SecretReferences, cancellationToken));

        string? container = null;
        try
        {
            await EnsureNetworkAsync(options.OutboundNetwork, isInternal: false, WorkspaceLabels.For(options.InstallationId, null, null, key), cancellationToken);
            await EnsureNetworkAsync(WorkspaceLabels.StepNetwork(request.ProbeId), isInternal: true, WorkspaceLabels.For(options.InstallationId, null, request.ProbeId, key), cancellationToken);
            await StartEgressProxyAsync(stepRequest, key, cancellationToken);
            container = (await docker.Containers.CreateContainerAsync(definition, cancellationToken)).ID;
            await docker.Containers.StartContainerAsync(container, new ContainerStartParameters(), cancellationToken);
            return await WaitForProbeAsync(container, request.Limit, cancellationToken);
        }
        finally
        {
            // What the probe made goes with it, whatever came of it. The container's environment held the credential.
            if (container is not null)
            {
                await RemoveQuietlyAsync(container);
            }

            await RemoveQuietlyAsync(WorkspaceLabels.EgressProxy(request.ProbeId));
            await RemoveNetworkQuietlyAsync(WorkspaceLabels.StepNetwork(request.ProbeId));
        }
    }

    /// <summary>
    /// Waits for the probe's container to end, up to the limit, and reads what it printed through the masker. A probe that is still running at its limit is not asked to stop: it is removed by force, which kills it, and
    /// the result says it timed out. Nothing of its output is kept beyond this call.
    /// </summary>
    private async Task<ProbeResult> WaitForProbeAsync(string container, TimeSpan limit, CancellationToken cancellationToken)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            var state = (await docker.Containers.InspectContainerAsync(container, cancellationToken)).State;
            if (state is { Running: false, Status: "exited" or "dead" })
            {
                var cause = state.OOMKilled ? StepExitCause.OutOfMemory : state.Status == "exited" ? StepExitCause.Exited : StepExitCause.Lost;
                return new ProbeResult(state.ExitCode, cause, await ReadProbeOutputAsync(container, cancellationToken));
            }

            if (waited.Elapsed >= limit)
            {
                return new ProbeResult(-1, StepExitCause.Timeout, []);
            }

            await Task.Delay(options.CheckoutPollInterval, cancellationToken);
        }
    }

    /// <summary>The lines the container printed, masked and cut to the last <see cref="WorkspaceRuntimeOptions.ProbeOutputLimit"/> characters, where the end of the run is; a line cut in two is dropped.</summary>
    private async Task<IReadOnlyList<string>> ReadProbeOutputAsync(string container, CancellationToken cancellationToken)
    {
        var text = masker.Mask(await ContainerLogs.ReadAsync(docker, container, cancellationToken));
        var cut = text.Length > options.ProbeOutputLimit;
        var lines = (cut ? text[^options.ProbeOutputLimit..] : text).Split('\n').AsEnumerable();
        return [.. (cut ? lines.Skip(1) : lines).Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0)];
    }

    private async Task RemoveNetworkQuietlyAsync(string network)
    {
        try
        {
            await docker.Networks.DeleteNetworkAsync(network, CancellationToken.None);
        }
        catch (Exception exception) when (exception is DockerApiException or HttpRequestException or IOException or TaskCanceledException)
        {
            // A network that cannot be removed now is found by its labels and removed with the others of its installation.
        }
    }
}
