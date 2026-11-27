using Hephaestus.Core.Ports;
using Hephaestus.Core.Trust;
using Hephaestus.GitHub;
using Hephaestus.Host.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hephaestus.Host.Tests.Hosting;

/// <summary>Story 2.1, NFR-12: the host runs the poller, and the gateway and the poller share one rate-limit gate.</summary>
public class PollerWiringTests
{
    [Fact]
    public async Task The_running_host_has_the_poller_service_among_its_hosted_services_and_the_poller_over_the_storage_ports()
    {
        await using var host = await RunningHost.StartAsync();

        Assert.Single(host.App.Services.GetServices<IHostedService>().OfType<PollerService>());
        Assert.NotNull(host.App.Services.GetRequiredService<GitHubPoller>());
        Assert.IsAssignableFrom<ISignalInbox>(host.App.Services.GetRequiredService<ISignalInbox>());
        Assert.IsAssignableFrom<IPollCursorStore>(host.App.Services.GetRequiredService<IPollCursorStore>());
    }

    [Fact]
    public async Task A_rate_limit_that_closes_the_hosts_gate_holds_back_the_gateways_commands_as_well()
    {
        await using var host = await RunningHost.StartAsync();
        var time = host.App.Services.GetRequiredService<TimeProvider>();
        var gate = host.App.Services.GetRequiredService<GitHubRateLimit>();
        var gateway = host.App.Services.GetRequiredService<ICodeHost>();

        // Not connected is what an open gate would answer; a closed one answers before it looks.
        Assert.Equal(GitHubUserFailure.NotConnected, (await gateway.FindUserAsync("alice", TestContext.Current.CancellationToken)).Failure);

        gate.CloseUntil(time.GetUtcNow().AddMinutes(5));

        Assert.Equal(GitHubUserFailure.Unreachable, (await gateway.FindUserAsync("alice", TestContext.Current.CancellationToken)).Failure);
    }
}
