using Hephaestus.Core.Localization;
using Hephaestus.Core.Readiness;
using Hephaestus.Host.Cli;

namespace Hephaestus.Host.Tests.Cli;

/// <summary>Story 3.10: a command that waits for the host's work waits longer than one that asks a question, and a call that is too slow is the host not answering.</summary>
public class ControlApiClientTimeoutTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void The_check_waits_longer_than_a_probe_may_take_and_an_ordinary_call_does_not()
    {
        Assert.True(ControlApiClient.CheckTimeout > AgentSignInService.ProbeTimeLimit + TimeSpan.FromMinutes(5));
        Assert.True(ControlApiClient.DefaultTimeout < AgentSignInService.ProbeTimeLimit);
    }

    [Fact]
    public async Task A_call_that_outlasts_its_limit_ends_as_a_host_that_did_not_answer()
    {
        await using var host = await RunningHost.StartAsync(mapMore: app => app.MapGet("/slow", async context => await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted)));
        using var client = new ControlApiClient(host.Settings.ControlSocketPath, Language.English);

        await Assert.ThrowsAsync<TaskCanceledException>(() => client.SendAsync(HttpMethod.Get, "/slow", null, Token, TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task A_call_given_more_time_waits_for_the_answer()
    {
        await using var host = await RunningHost.StartAsync(mapMore: app => app.MapGet("/slow", async context =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), context.RequestAborted);
            await context.Response.WriteAsync("done", context.RequestAborted);
        }));
        using var client = new ControlApiClient(host.Settings.ControlSocketPath, Language.English);

        var answer = await client.SendAsync(HttpMethod.Get, "/slow", null, Token, TimeSpan.FromSeconds(20));

        Assert.Equal("done", answer.Body);
    }

    [Fact]
    public async Task A_call_the_caller_cancels_is_cancelled_and_not_a_timeout()
    {
        await using var host = await RunningHost.StartAsync(mapMore: app => app.MapGet("/slow", async context => await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted)));
        using var client = new ControlApiClient(host.Settings.ControlSocketPath, Language.English);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(HttpMethod.Get, "/slow", null, cancel.Token));

        Assert.True(cancel.IsCancellationRequested);
        Assert.NotNull(thrown);
    }
}
