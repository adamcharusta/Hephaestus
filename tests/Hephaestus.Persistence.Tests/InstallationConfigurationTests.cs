using Hephaestus.Core.Aliases;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Protection;
using Hephaestus.Persistence.Model;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Persistence.Tests;

/// <summary>Story 1.9, AD-16: Aliases, limits, protected paths, and settings are written in their own transactions, read through the read store, and outlive a restart.</summary>
public class InstallationConfigurationTests
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
    public async Task A_new_installation_has_the_two_default_protected_paths_no_limits_no_aliases_and_no_settings()
    {
        using var installation = await StartAsync();

        Assert.Equal(ProtectedPath.Defaults.Order(StringComparer.Ordinal), await installation.Reads.ListProtectedPathsAsync(Token));
        Assert.Equal(InstallationLimits.None, await installation.Reads.GetLimitsAsync(Token));
        Assert.Empty(await installation.Reads.ListAliasesAsync(Token));
        Assert.Empty(await installation.Reads.ListSettingsAsync(Token));
    }

    [Fact]
    public async Task An_Alias_for_an_Agent_and_a_Model_is_stored_and_listed_with_the_time_it_was_added()
    {
        using var installation = await StartAsync();

        var written = await installation.Configuration.AddAliasAsync(Alias.ForModel("opus", Agent.ClaudeCode, "claude-opus-5-5", Noon), Token);

        Assert.True(written.Added);
        var listed = Assert.Single(await installation.Reads.ListAliasesAsync(Token));
        Assert.Equal("opus", listed.Name);
        Assert.Equal(Agent.ClaudeCode, listed.Agent);
        Assert.Equal("claude-opus-5-5", listed.Model);
        Assert.Null(listed.Effort);
        Assert.Equal(Noon, listed.AddedAt);
    }

    [Fact]
    public async Task An_Effort_Alias_is_stored_without_an_Agent_or_a_Model()
    {
        using var installation = await StartAsync();

        await installation.Configuration.AddAliasAsync(Alias.ForEffort("deep", Effort.ExtraHigh, Noon), Token);

        var listed = Assert.Single(await installation.Reads.ListAliasesAsync(Token));
        Assert.Equal(Effort.ExtraHigh, listed.Effort);
        Assert.Null(listed.Agent);
        Assert.Null(listed.Model);
    }

    [Fact]
    public async Task Adding_a_name_that_is_stored_returns_the_stored_Alias_and_changes_nothing()
    {
        using var installation = await StartAsync();
        await installation.Configuration.AddAliasAsync(Alias.ForModel("opus", Agent.ClaudeCode, "claude-opus-5-5", Noon), Token);

        var again = await installation.Configuration.AddAliasAsync(Alias.ForModel("opus", Agent.CodexCli, "gpt-5.5", Noon.AddDays(1)), Token);

        Assert.False(again.Added);
        Assert.Equal(Agent.ClaudeCode, again.Alias.Agent);
        Assert.Equal(Noon, again.Alias.AddedAt);
        Assert.Equal("claude-opus-5-5", Assert.Single(await installation.Reads.ListAliasesAsync(Token)).Model);
    }

    [Fact]
    public async Task Aliases_are_listed_by_name_and_removing_one_returns_it_and_a_second_removal_finds_nothing()
    {
        using var installation = await StartAsync();
        await installation.Configuration.AddAliasAsync(Alias.ForEffort("zeta", Effort.Low, Noon), Token);
        await installation.Configuration.AddAliasAsync(Alias.ForModel("alpha", Agent.CodexCli, "gpt-5.5", Noon), Token);

        Assert.Equal(["alpha", "zeta"], (await installation.Reads.ListAliasesAsync(Token)).Select(a => a.Name));
        var removed = await installation.Configuration.RemoveAliasAsync("alpha", Token);
        var again = await installation.Configuration.RemoveAliasAsync("alpha", Token);

        Assert.Equal("gpt-5.5", removed?.Model);
        Assert.Null(again);
        Assert.Equal(["zeta"], (await installation.Reads.ListAliasesAsync(Token)).Select(a => a.Name));
    }

    [Fact]
    public async Task The_table_itself_refuses_an_Alias_that_names_both_or_neither_an_Agent_and_an_Effort()
    {
        using var installation = await StartAsync();
        await using var context = installation.Database.OpenWriter();

        foreach (var values in new[] { "'a', 'claude-code', 'm', 'high'", "'b', NULL, NULL, NULL", "'c', 'claude-code', NULL, NULL", "'d', NULL, 'm', 'high'" })
        {
            var insert = $"""INSERT INTO "Aliases" ("Name", "Agent", "Model", "Effort", "AddedAt") VALUES ({values}, '2026-11-27T12:00:00.0000000Z')""";
            await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync(insert, Token));
        }
    }

    [Fact]
    public async Task Concurrent_additions_of_one_name_store_it_once_and_one_writer_wins()
    {
        using var installation = await StartAsync();

        var writes = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            installation.Configuration.AddAliasAsync(Alias.ForModel("opus", Agent.ClaudeCode, $"model-{i}", Noon), Token)));

        Assert.Single(writes, w => w.Added);
        Assert.Single(await installation.Reads.ListAliasesAsync(Token));
        Assert.All(writes, w => Assert.Equal(writes.Single(x => x.Added).Alias.Model, w.Alias.Model));
    }

    [Fact]
    public async Task Concurrent_removals_of_one_name_return_the_Alias_once_and_never_fail()
    {
        using var installation = await StartAsync();
        await installation.Configuration.AddAliasAsync(Alias.ForModel("opus", Agent.ClaudeCode, "claude-opus-5-5", Noon), Token);

        var removals = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => installation.Configuration.RemoveAliasAsync("opus", Token)));

        Assert.Single(removals, removed => removed is not null);
        Assert.Empty(await installation.Reads.ListAliasesAsync(Token));
    }

    [Fact]
    public async Task A_removal_racing_additions_finds_the_Alias_or_nothing_and_never_fails()
    {
        using var installation = await StartAsync();

        var work = Enumerable.Range(0, 20).Select(async i =>
        {
            await installation.Configuration.AddAliasAsync(Alias.ForEffort($"a{i % 4}", Effort.Low, Noon), Token);
            await installation.Configuration.RemoveAliasAsync($"a{i % 4}", Token);
        });

        await Task.WhenAll(work);
    }

    [Fact]
    public async Task Limits_start_unset_and_a_change_keeps_the_limits_it_does_not_name()
    {
        using var installation = await StartAsync();

        var first = await installation.Configuration.SetLimitsAsync(new LimitsChange(TimeSpan.FromHours(2), 2000, 4L << 30), Token);
        var second = await installation.Configuration.SetLimitsAsync(new LimitsChange(null, null, 8L << 30), Token);
        var third = await installation.Configuration.SetLimitsAsync(new LimitsChange(TimeSpan.FromMinutes(90), null, null), Token);

        Assert.Equal(new InstallationLimits(TimeSpan.FromHours(2), 2000, 4L << 30), first);
        Assert.Equal(new InstallationLimits(TimeSpan.FromHours(2), 2000, 8L << 30), second);
        Assert.Equal(new InstallationLimits(TimeSpan.FromMinutes(90), 2000, 8L << 30), third);
        Assert.Equal(third, await installation.Reads.GetLimitsAsync(Token));
    }

    [Fact]
    public async Task A_change_that_names_one_limit_on_a_new_installation_leaves_the_others_unset()
    {
        using var installation = await StartAsync();

        var limits = await installation.Configuration.SetLimitsAsync(new LimitsChange(null, 500, null), Token);

        Assert.Equal(new InstallationLimits(null, 500, null), limits);
    }

    [Fact]
    public async Task The_limits_table_holds_one_row()
    {
        using var installation = await StartAsync();
        await installation.Configuration.SetLimitsAsync(new LimitsChange(null, 500, null), Token);
        await using var context = installation.Database.OpenWriter();

        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync("""INSERT INTO "InstallationLimits" ("Id") VALUES (2)""", Token));
    }

    [Fact]
    public async Task Protected_paths_are_added_once_listed_in_order_and_removed()
    {
        using var installation = await StartAsync();

        Assert.True(await installation.Configuration.AddProtectedPathAsync("docs/**", Token));
        Assert.False(await installation.Configuration.AddProtectedPathAsync("docs/**", Token));
        Assert.Equal([".github/**", "docs/**", "hephaestus.yml"], await installation.Reads.ListProtectedPathsAsync(Token));
        Assert.True(await installation.Configuration.RemoveProtectedPathAsync(".github/**", Token));
        Assert.False(await installation.Configuration.RemoveProtectedPathAsync(".github/**", Token));
        Assert.Equal(["docs/**", "hephaestus.yml"], await installation.Reads.ListProtectedPathsAsync(Token));
    }

    [Fact]
    public async Task The_Owner_may_empty_the_list_and_it_stays_empty_after_a_restart()
    {
        using var installation = await StartAsync();
        await installation.Configuration.RemoveProtectedPathAsync(".github/**", Token);
        await installation.Configuration.RemoveProtectedPathAsync("hephaestus.yml", Token);

        await using var restarted = installation.Restart();
        await restarted.GetRequiredService<DatabaseMigrator>().MigrateAsync(Token);

        Assert.Empty(await restarted.GetRequiredService<IReadStore>().ListProtectedPathsAsync(Token));
    }

    [Fact]
    public async Task A_setting_is_stored_replaced_and_read_by_name()
    {
        using var installation = await StartAsync();

        await installation.Configuration.SetSettingAsync("language", "pl", Token);
        await installation.Configuration.SetSettingAsync("language", "en", Token);
        await installation.Configuration.SetSettingAsync("language", "pl", Token);

        Assert.Equal(new Dictionary<string, string> { ["language"] = "pl" }, await installation.Reads.ListSettingsAsync(Token));
    }

    [Fact]
    public async Task Everything_is_unchanged_after_a_restart()
    {
        using var installation = await StartAsync();
        await installation.Configuration.AddAliasAsync(Alias.ForModel("opus", Agent.ClaudeCode, "claude-opus-5-5", Noon), Token);
        await installation.Configuration.AddAliasAsync(Alias.ForEffort("deep", Effort.High, Noon), Token);
        await installation.Configuration.SetLimitsAsync(new LimitsChange(TimeSpan.FromHours(1), 1500, 2L << 30), Token);
        await installation.Configuration.AddProtectedPathAsync("docs/**", Token);
        await installation.Configuration.SetSettingAsync("language", "pl", Token);

        await using var restarted = installation.Restart();
        await restarted.GetRequiredService<DatabaseMigrator>().MigrateAsync(Token);
        var reads = restarted.GetRequiredService<IReadStore>();

        Assert.Equal(await installation.Reads.ListAliasesAsync(Token), await reads.ListAliasesAsync(Token));
        Assert.Equal(new InstallationLimits(TimeSpan.FromHours(1), 1500, 2L << 30), await reads.GetLimitsAsync(Token));
        Assert.Equal([".github/**", "docs/**", "hephaestus.yml"], await reads.ListProtectedPathsAsync(Token));
        Assert.Equal("pl", (await reads.ListSettingsAsync(Token))["language"]);
    }

    [Fact]
    public async Task A_stored_Alias_that_names_neither_a_known_Agent_nor_a_known_Effort_is_reported_not_guessed()
    {
        using var installation = await StartAsync();
        await using (var context = installation.Database.OpenWriter())
        {
            await context.Database.ExecuteSqlRawAsync(
                """INSERT INTO "Aliases" ("Name", "Agent", "Model", "Effort", "AddedAt") VALUES ('odd', 'gemini', 'm', NULL, '2026-11-27T12:00:00.0000000Z')""",
                Token);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => installation.Reads.ListAliasesAsync(Token));
    }

    [Fact]
    public async Task The_keys_are_the_names_the_globs_and_the_single_limits_row()
    {
        using var installation = await StartAsync();
        await using var context = installation.Database.OpenReader();

        Assert.Equal(["Name"], context.Model.FindEntityType(typeof(AliasEntry))!.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(["Pattern"], context.Model.FindEntityType(typeof(ProtectedPathEntry))!.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(["Name"], context.Model.FindEntityType(typeof(SettingEntry))!.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
