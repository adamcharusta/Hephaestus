using Hephaestus.Core.Aliases;
using Hephaestus.Core.Invocations;
using Hephaestus.Core.Localization;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Readiness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Persistence.Tests;

/// <summary>Story 3.10, FR-25, AD-16, AD-20: how an Agent signs in, and the capacity record of each Agent credential, are stored without a credential and outlive a restart.</summary>
public class AgentSetupTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<TempInstallation> StartAsync()
    {
        var installation = new TempInstallation();
        await installation.MigrateAsync();
        return installation;
    }

    [Fact]
    public async Task A_new_installation_has_chosen_no_authentication_and_has_no_capacity_record()
    {
        using var installation = await StartAsync();

        Assert.Empty(await installation.Reads.ListAgentSettingsAsync(Token));
        Assert.Empty(await installation.Reads.ListAgentCapacitiesAsync(Token));
    }

    [Fact]
    public async Task The_mode_an_Owner_chose_is_stored_with_the_time_and_replaced_by_the_next_choice()
    {
        using var installation = await StartAsync();

        await installation.Configuration.SetAgentAuthenticationAsync(Agent.ClaudeCode, AuthenticationMode.ApiKey, Noon, credentialStored: true, Token);
        await installation.Configuration.SetAgentAuthenticationAsync(Agent.ClaudeCode, AuthenticationMode.Subscription, Noon.AddHours(1), credentialStored: true, Token);

        var setting = Assert.Single(await installation.Reads.ListAgentSettingsAsync(Token));
        Assert.Equal(new AgentSetting(Agent.ClaudeCode, AuthenticationMode.Subscription, Noon.AddHours(1)), setting);
    }

    [Fact]
    public async Task Every_Agent_has_a_mode_of_its_own_and_they_are_listed_in_order()
    {
        using var installation = await StartAsync();

        await installation.Configuration.SetAgentAuthenticationAsync(Agent.CodexCli, AuthenticationMode.ApiKey, Noon, credentialStored: true, Token);
        await installation.Configuration.SetAgentAuthenticationAsync(Agent.ClaudeCode, AuthenticationMode.Subscription, Noon, credentialStored: true, Token);

        Assert.Equal([Agent.ClaudeCode, Agent.CodexCli], (await installation.Reads.ListAgentSettingsAsync(Token)).Select(s => s.Agent));
    }

    [Fact]
    public async Task A_probe_is_stored_on_the_record_of_the_credential_with_its_reason_and_time()
    {
        using var installation = await StartAsync();

        await installation.Configuration.RecordAgentProbeAsync(Agent.ClaudeCode, AuthenticationMode.ApiKey, AgentReadiness.Unavailable, MessageIds.AgentProbeError, ["claude-code", "1"], Noon, Token);

        var capacity = Assert.Single(await installation.Reads.ListAgentCapacitiesAsync(Token));
        Assert.Equal(Agent.ClaudeCode, capacity.Agent);
        Assert.Equal(AuthenticationMode.ApiKey, capacity.Mode);
        Assert.Equal(AgentReadiness.Unavailable, capacity.Readiness);
        Assert.Equal(MessageIds.AgentProbeError, capacity.ReasonId);
        Assert.Equal(["claude-code", "1"], capacity.ReasonArguments);
        Assert.Equal(Noon, capacity.ProbedAt);
        Assert.Null(capacity.ExhaustedUntil);
        Assert.False(capacity.HasCapacity(Noon));
    }

    [Fact]
    public async Task A_later_probe_replaces_the_finding_of_the_same_credential_and_leaves_other_credentials_alone()
    {
        using var installation = await StartAsync();
        await installation.Configuration.RecordAgentProbeAsync(Agent.ClaudeCode, AuthenticationMode.ApiKey, AgentReadiness.Unavailable, MessageIds.AgentProbeError, ["claude-code", "1"], Noon, Token);
        await installation.Configuration.RecordAgentProbeAsync(Agent.ClaudeCode, AuthenticationMode.Subscription, AgentReadiness.Unavailable, MessageIds.AgentProbeUnknown, ["claude-code", "2"], Noon, Token);

        await installation.Configuration.RecordAgentProbeAsync(Agent.ClaudeCode, AuthenticationMode.ApiKey, AgentReadiness.Ready, null, [], Noon.AddHours(1), Token);

        var capacities = await installation.Reads.ListAgentCapacitiesAsync(Token);
        var ready = capacities.Single(c => c.Mode == AuthenticationMode.ApiKey);
        Assert.Equal(AgentReadiness.Ready, ready.Readiness);
        Assert.Null(ready.ReasonId);
        Assert.Empty(ready.ReasonArguments);
        Assert.Null(ready.Reason);
        Assert.Equal(Noon.AddHours(1), ready.ProbedAt);
        Assert.True(ready.HasCapacity(Noon.AddHours(2)));
        Assert.Equal(MessageIds.AgentProbeUnknown, capacities.Single(c => c.Mode == AuthenticationMode.Subscription).ReasonId);
    }

    [Fact]
    public async Task A_probe_keeps_the_time_until_which_a_usage_limit_holds_the_Agent()
    {
        using var installation = await StartAsync();
        await installation.Configuration.RecordAgentProbeAsync(Agent.ClaudeCode, AuthenticationMode.ApiKey, AgentReadiness.Ready, null, [], Noon, Token);
        await using (var context = installation.Database.OpenWriter())
        {
            await context.Database.ExecuteSqlRawAsync("""UPDATE "AgentCapacities" SET "ExhaustedUntil" = '2026-11-28T00:00:00.0000000Z'""", Token);
        }

        await installation.Configuration.RecordAgentProbeAsync(Agent.ClaudeCode, AuthenticationMode.ApiKey, AgentReadiness.Ready, null, [], Noon.AddHours(1), Token);

        var capacity = Assert.Single(await installation.Reads.ListAgentCapacitiesAsync(Token));
        Assert.Equal(new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero), capacity.ExhaustedUntil);
        Assert.False(capacity.HasCapacity(Noon.AddHours(2)));
        Assert.True(capacity.HasCapacity(new DateTimeOffset(2026, 11, 28, 0, 0, 1, TimeSpan.Zero)));
    }

    [Fact]
    public async Task Storing_a_credential_forgets_what_a_probe_found_of_the_credential_of_that_mode_and_only_that()
    {
        using var installation = await StartAsync();
        await installation.Configuration.RecordAgentProbeAsync(Agent.ClaudeCode, AuthenticationMode.ApiKey, AgentReadiness.Unavailable, MessageIds.AgentProbeError, ["claude-code", "1"], Noon, Token);
        await installation.Configuration.RecordAgentProbeAsync(Agent.ClaudeCode, AuthenticationMode.Subscription, AgentReadiness.Ready, null, [], Noon, Token);
        await installation.Configuration.RecordAgentProbeAsync(Agent.CodexCli, AuthenticationMode.ApiKey, AgentReadiness.Ready, null, [], Noon, Token);

        await installation.Configuration.SetAgentAuthenticationAsync(Agent.ClaudeCode, AuthenticationMode.ApiKey, Noon.AddHours(1), credentialStored: true, Token);

        var left = await installation.Reads.ListAgentCapacitiesAsync(Token);
        Assert.Equal([(Agent.ClaudeCode, AuthenticationMode.Subscription), (Agent.CodexCli, AuthenticationMode.ApiKey)], left.Select(c => (c.Agent, c.Mode)));
    }

    [Fact]
    public async Task Keeping_the_credential_keeps_what_a_probe_found_and_the_usage_limit_that_holds_it()
    {
        using var installation = await StartAsync();
        await installation.Configuration.SetAgentAuthenticationAsync(Agent.ClaudeCode, AuthenticationMode.ApiKey, Noon, credentialStored: true, Token);
        await installation.Configuration.RecordAgentProbeAsync(Agent.ClaudeCode, AuthenticationMode.ApiKey, AgentReadiness.Ready, null, [], Noon, Token);
        await using (var context = installation.Database.OpenWriter())
        {
            await context.Database.ExecuteSqlRawAsync("""UPDATE "AgentCapacities" SET "ExhaustedUntil" = '2026-11-28T00:00:00.0000000Z'""", Token);
        }

        await installation.Configuration.SetAgentAuthenticationAsync(Agent.ClaudeCode, AuthenticationMode.ApiKey, Noon.AddHours(1), credentialStored: false, Token);

        var capacity = Assert.Single(await installation.Reads.ListAgentCapacitiesAsync(Token));
        Assert.Equal(AgentReadiness.Ready, capacity.Readiness);
        Assert.Equal(new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero), capacity.ExhaustedUntil);
        Assert.Equal(Noon.AddHours(1), Assert.Single(await installation.Reads.ListAgentSettingsAsync(Token)).ChosenAt);
    }

    [Fact]
    public async Task What_was_stored_outlives_a_restart()
    {
        using var installation = await StartAsync();
        await installation.Configuration.SetAgentAuthenticationAsync(Agent.ClaudeCode, AuthenticationMode.Subscription, Noon, credentialStored: true, Token);
        await installation.Configuration.RecordAgentProbeAsync(Agent.ClaudeCode, AuthenticationMode.Subscription, AgentReadiness.Ready, null, [], Noon, Token);

        using var restarted = installation.Restart();
        var reads = restarted.GetRequiredService<IReadStore>();

        Assert.Equal(AuthenticationMode.Subscription, Assert.Single(await reads.ListAgentSettingsAsync(Token)).Mode);
        Assert.Equal(AgentReadiness.Ready, Assert.Single(await reads.ListAgentCapacitiesAsync(Token)).Readiness);
    }
}
