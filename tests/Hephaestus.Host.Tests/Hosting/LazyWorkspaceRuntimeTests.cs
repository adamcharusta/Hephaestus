using Hephaestus.Core.AgentBinaries;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Validation;
using Hephaestus.Core.Workspaces;
using Hephaestus.Host.Hosting;
using NSubstitute;

namespace Hephaestus.Host.Tests.Hosting;

/// <summary>Story 3.14, AD-7: the loop holds the runtime that the host registered, found when it first asks for something, so that a host that starts no Task needs neither the lock of the Agent binaries nor the Docker client.</summary>
public class LazyWorkspaceRuntimeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly ResolvedImage Image = new("ubuntu:24.04", "sha256:" + new string('1', 64), AgentArchitecture.X64, CLibrary.Glibc);

    [Fact]
    public async Task Nothing_is_resolved_until_the_runtime_is_asked_for_something_and_then_every_call_goes_to_the_runtime_of_the_host()
    {
        var resolved = 0;
        var real = Substitute.For<IWorkspaceRuntime>();
        real.ResolveImageAsync("ubuntu:24.04", Token).Returns(Image);
        real.StartStepAsync(null!, "key", Token).Returns(new StartedStep("c", "n", true));
        real.StartValidationAsync(null!, "key", Token).Returns(new StartedStep("v", "n", true));
        real.CheckpointAsync(null!, "key", Token).Returns(CheckpointResult.Unchanged);
        real.RunProbeAsync(null!, Token).Returns(new ProbeResult(0, StepExitCause.Exited, []));
        var services = new ServiceCollection().AddTransient<IWorkspaceRuntime>(_ =>
        {
            resolved++;
            return real;
        }).BuildServiceProvider();
        var lazy = new LazyWorkspaceRuntime(services);
        Assert.Equal(0, resolved);

        // The requests are only passed on, so the test passes none that it would have to build.
        Assert.Equal(Image, await lazy.ResolveImageAsync("ubuntu:24.04", Token));
        await lazy.EnsureTaskVolumeAsync(Guid.CreateVersion7(), Image, "key", Token);
        Assert.Equal("c", (await lazy.StartStepAsync(null!, "key", Token)).ContainerId);
        Assert.Equal("v", (await lazy.StartValidationAsync(null!, "key", Token)).ContainerId);
        await lazy.CheckoutAsync(null!, "key", Token);
        Assert.True((await lazy.CheckpointAsync(null!, "key", Token)).IsUnchanged);
        Assert.Equal(0, (await lazy.RunProbeAsync(null!, Token)).ExitCode);

        Assert.Equal(7, resolved);
        await real.Received(1).EnsureTaskVolumeAsync(Arg.Any<Guid>(), Image, "key", Token);
        await real.Received(1).CheckoutAsync(null!, "key", Token);
    }
}
