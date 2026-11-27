using System.Formats.Tar;
using System.Runtime.InteropServices;
using Docker.DotNet;
using Docker.DotNet.Models;
using Hephaestus.Core.AgentBinaries;

namespace Hephaestus.Workspaces.Tests;

/// <summary>Whether a Docker Engine answers on this machine; the tests that need one are skipped where none does (CI runs them).</summary>
internal static class RealEngine
{
    public static bool Available { get; } = Probe();

    /// <summary>Continuous integration, where a missing Engine, or one that cannot be reached, is a failure and not a reason to skip: the claims about step containers are shown by these tests alone.</summary>
    public static bool Required { get; } = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

    private static bool Probe()
    {
        try
        {
            using var client = new DockerClientBuilder().WithTimeout(TimeSpan.FromSeconds(10)).Build();
            client.System.PingAsync(CancellationToken.None).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception exception) when (exception is DockerApiException or HttpRequestException or IOException or TimeoutException or TaskCanceledException or InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>
/// An installation of its own on the real Docker Engine (AD-5, AR-34): its own installation id, workspace network, and agents volume, filled with the stand-in
/// executables of <see cref="FakeVendor"/> in the layout the host leaves. Everything the tests create carries the installation label, and disposing
/// removes whatever carries it, which is also how recovery and cleanup select.
/// </summary>
public sealed class EngineFixture : IAsyncLifetime
{
    public const string Musl = "alpine:3.23";
    public const string Glibc = "debian:13-slim";

    private readonly string _suffix = Guid.NewGuid().ToString("N")[..12];

    public string InstallationId => $"hx-test-{_suffix}";

    public string AgentsVolume => $"hephaestus-agents-test-{_suffix}";

    /// <summary>The network with a route out that the egress proxies of the tests are attached to; the networks of the step runs are named after the step runs.</summary>
    public string OutboundNetwork => $"hephaestus-outbound-test-{_suffix}";

    /// <summary>
    /// The <c>hephaestus</c> image the egress proxies are created from, which the tests need to start a step: continuous integration builds it before the tests run and names it in
    /// <see cref="ProxyImageVariable"/>. Where nothing names one, no step can be started and the tests that start one are skipped.
    /// </summary>
    public string? ProxyImage { get; } = Environment.GetEnvironmentVariable(ProxyImageVariable);

    public const string ProxyImageVariable = "HEPHAESTUS_TEST_PROXY_IMAGE";

    public IDockerClient Docker { get; private set; }

    internal FakeVendor Vendor { get; } = new();

    public AgentArchitecture HostArchitecture { get; } = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? AgentArchitecture.Arm64 : AgentArchitecture.X64;

    public DockerWorkspaceRuntime Runtime { get; private set; }

    /// <summary>The options the fixture's runtime was built with, for a test that builds a runtime of its own on the same installation.</summary>
    public WorkspaceRuntimeOptions RuntimeOptions { get; private set; }

    public async ValueTask InitializeAsync()
    {
        if (!RealEngine.Available)
        {
            return;
        }

        Docker = new DockerClientBuilder().WithTimeout(TimeSpan.FromMinutes(10)).Build();
        RuntimeOptions = new WorkspaceRuntimeOptions(InstallationId, Vendor.Lock, HostArchitecture) { AgentsVolume = AgentsVolume, OutboundNetwork = OutboundNetwork, EgressProxyImage = ProxyImage ?? "hephaestus.invalid/none:0", EgressProxyPollInterval = TimeSpan.FromMilliseconds(100), StepPollInterval = TimeSpan.FromMilliseconds(250) };
        Runtime = new DockerWorkspaceRuntime(Docker, RuntimeOptions, new RecordingWatcher());
        var image = await Runtime.ResolveImageAsync(Musl, TestContext.Current.CancellationToken);
        await FillAgentsVolumeAsync(image.Reference, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Docker is null)
        {
            return;
        }

        var label = new Dictionary<string, IDictionary<string, bool>> { ["label"] = new Dictionary<string, bool> { [$"hephaestus.installation={InstallationId}"] = true } };
        foreach (var container in await Docker.Containers.ListContainersAsync(new ContainersListParameters { All = true, Filters = label }, CancellationToken.None))
        {
            await Docker.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters { Force = true }, CancellationToken.None);
        }

        foreach (var volume in (await Docker.Volumes.ListAsync(new VolumesListParameters { Filters = label }, CancellationToken.None)).Volumes ?? [])
        {
            await Docker.Volumes.RemoveAsync(volume.Name, true, CancellationToken.None);
        }

        foreach (var network in await Docker.Networks.ListNetworksAsync(new NetworksListParameters { Filters = label }, CancellationToken.None))
        {
            await Docker.Networks.DeleteNetworkAsync(network.ID, CancellationToken.None);
        }

        Docker.Dispose();
    }

    /// <summary>The agents volume as the host leaves it: every executable of this architecture at its place, and the certificate bundle.</summary>
    private async Task FillAgentsVolumeAsync(string image, CancellationToken cancellationToken)
    {
        var labels = new Dictionary<string, string> { ["hephaestus.installation"] = InstallationId, ["hephaestus.task"] = string.Empty, ["hephaestus.step-run"] = string.Empty };
        await Docker.Volumes.CreateAsync(new VolumesCreateParameters { Name = AgentsVolume, Labels = labels }, cancellationToken);
        var helper = await Docker.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Name = $"hephaestus-agents-fill-{_suffix}",
                Image = image,
                Labels = labels,
                HostConfig = new HostConfig { NetworkMode = "none", Mounts = [new Mount { Type = "volume", Source = AgentsVolume, Target = "/agents" }] },
            },
            cancellationToken);
        using var archive = new MemoryStream();
        using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var binary in Vendor.Lock.For(HostArchitecture))
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, binary.RelativePath)
                {
                    Mode = (UnixFileMode)0b101_101_101,
                    DataStream = new MemoryStream(Vendor.Executable(binary)),
                });
            }

            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "certs/ca-certificates.crt") { Mode = (UnixFileMode)0b100_100_100, DataStream = new MemoryStream([1]) });
        }

        archive.Position = 0;
        await Docker.Containers.ExtractArchiveToContainerAsync(helper.ID, new CopyToContainerParameters { Path = "/agents", CopyUIDGID = true }, archive, cancellationToken);
        await Docker.Containers.RemoveContainerAsync(helper.ID, new ContainerRemoveParameters { Force = true }, cancellationToken);
    }
}
