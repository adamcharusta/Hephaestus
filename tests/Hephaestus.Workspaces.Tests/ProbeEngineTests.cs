using Docker.DotNet.Models;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Readiness;
using Hephaestus.Core.Workspaces;
using SecretReference = Hephaestus.Core.Invocations.SecretReference;

namespace Hephaestus.Workspaces.Tests;

/// <summary>
/// Story 3.10 against a real Docker Engine (AD-20, AD-7): the probe image meets the Project image contract, a probe resolves its credential in the container and in nothing else, has no Task volume, and leaves
/// nothing behind. Skipped where no Engine answers and run in CI.
/// </summary>
public sealed class ProbeEngineTests(EngineFixture engine) : IClassFixture<EngineFixture>
{
    private const string Credential = "probe-credential-0123456789";
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task The_probe_image_is_a_glibc_image_that_declares_no_volume_and_has_bash()
    {
        var image = await engine.Runtime.ResolveImageAsync(ProbeImage.Reference, Cancel);

        Assert.Equal(CLibrary.Glibc, image.Libc);
        Assert.Contains("@sha256:", image.Reference, StringComparison.Ordinal);
        var inspected = await engine.Docker.Images.InspectImageAsync(image.Reference, Cancel);
        Assert.Empty(inspected.Config?.Volumes ?? new Dictionary<string, EmptyStruct>());
        var created = await engine.Docker.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Name = $"hephaestus-probe-image-test-{Guid.NewGuid():N}",
                Image = image.Reference,
                Labels = new Dictionary<string, string> { ["hephaestus.installation"] = engine.InstallationId },
                Entrypoint = ["/bin/bash"],
            },
            Cancel);
        var archive = await engine.Docker.Containers.GetArchiveFromContainerAsync(created.ID, new ContainerPathStatParameters { Path = "/bin/bash" }, false, Cancel);
        archive.Stream?.Dispose();
    }

    [Fact(Skip = "No Docker Engine answers on this machine.", SkipUnless = nameof(RealEngine.Available), SkipType = typeof(RealEngine))]
    public async Task A_probe_gets_its_credential_in_its_environment_with_no_Task_volume_and_leaves_nothing_behind()
    {
        EngineWorkspaces.Unless(engine.ProxyImage is not null, $"{EngineFixture.ProxyImageVariable} names no hephaestus image to create the egress proxies from.");
        var secrets = new TestSecrets().With("probe-secret", Credential);
        await secrets.ReadyAsync();
        var runtime = Runtimes.Of(engine.Docker, engine.RuntimeOptions, new RecordingWatcher(), secrets);
        var probeId = Guid.CreateVersion7();
        var invocation = new AgentInvocation(
            ["/bin/bash", "-c", "echo \"credential:$PROBE_CREDENTIAL\"; echo \"home:$HOME\"; test -e /hephaestus/context || echo no-context; test -e /hephaestus/methodology || echo no-methodology; ls /hephaestus/agents; exit 7"],
            new Dictionary<string, string>(),
            [new SecretReference("probe-secret", "PROBE_CREDENTIAL")],
            [],
            ["probe.invalid"],
            ResultLocation.InOutputStream);

        var result = await runtime.RunProbeAsync(new ProbeRequest(probeId, invocation, new StepLimits(1000, 256L * 1024 * 1024), TimeSpan.FromMinutes(2)), Cancel);

        Assert.Equal(StepExitCause.Exited, result.Cause);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("credential:***", result.Lines);
        Assert.DoesNotContain(result.Lines, line => line.Contains(Credential, StringComparison.Ordinal));
        Assert.Contains("home:/hephaestus/home", result.Lines);
        Assert.Contains("no-context", result.Lines);
        Assert.Contains("no-methodology", result.Lines);
        Assert.Contains("claude-code", result.Lines);
        Assert.Equal(["probe-secret"], secrets.Reads);

        var label = new Dictionary<string, IDictionary<string, bool>> { ["label"] = new Dictionary<string, bool> { [$"hephaestus.installation={engine.InstallationId}"] = true, ["hephaestus.role=agent-probe"] = true } };
        Assert.Empty(await engine.Docker.Containers.ListContainersAsync(new ContainersListParameters { All = true, Filters = label }, Cancel));
        Assert.DoesNotContain((await engine.Docker.Networks.ListNetworksAsync(new NetworksListParameters(), Cancel)).Select(network => network.Name), name => name == $"hephaestus-net-{probeId}");
        Assert.DoesNotContain((await engine.Docker.Volumes.ListAsync(Cancel)).Volumes ?? [], volume => volume.Name.Contains(probeId.ToString("D"), StringComparison.Ordinal));
    }
}
