using Hephaestus.Core.Ports;
using Hephaestus.Core.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Persistence.Tests;

/// <summary>Story 1.6: the store lists every value for the masker and reports each change before the call that made it returns.</summary>
public class SecretChangeTests
{
    private const string Value = "sk-test-0123456789-abcdefghij";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_stored_value_is_read_without_its_name_and_a_file_that_is_not_a_secret_is_left_out()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        await installation.Secrets.SetAsync("first", Value, Token);
        await installation.Secrets.SetAsync("second", Value + "-2", Token);
        await File.WriteAllTextAsync(Path.Combine(installation.Options.SecretsDirectory, ".abandoned.tmp"), "temporary-value-1234", Token);
        await File.WriteAllTextAsync(Path.Combine(installation.Options.SecretsDirectory, "Not A Name"), "other-file-value-1234", Token);

        var values = await installation.Secrets.ReadAllAsync(Token);

        Assert.Equal([Value, Value + "-2"], values.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task No_value_is_read_before_a_secrets_directory_exists()
    {
        using var installation = new TempInstallation();

        Assert.Empty(await installation.Secrets.ReadAllAsync(Token));
    }

    [Fact]
    public async Task Set_and_remove_raise_changed_and_wait_for_the_handler_before_they_return()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        var seen = new List<string>();
        installation.Secrets.Changed += async () =>
        {
            await Task.Delay(20, Token);
            seen.Add(string.Join(",", await installation.Secrets.ReadAllAsync(Token)));
        };

        await installation.Secrets.SetAsync("token", Value, Token);
        seen.Add("set returned");
        await installation.Secrets.RemoveAsync("token", Token);
        seen.Add("remove returned");

        Assert.Equal([Value, "set returned", string.Empty, "remove returned"], seen);
    }

    [Fact]
    public async Task Removing_a_secret_that_was_never_stored_changes_nothing_and_raises_nothing()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        var raised = 0;
        installation.Secrets.Changed += () =>
        {
            raised++;
            return Task.CompletedTask;
        };

        Assert.False(await installation.Secrets.RemoveAsync("unknown", Token));
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task Replacing_a_value_raises_changed_and_the_masker_masks_the_new_value_and_releases_the_old()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        var masker = new SecretMasker(installation.Secrets);
        var raised = 0;
        installation.Secrets.Changed += () =>
        {
            raised++;
            return Task.CompletedTask;
        };
        await installation.Secrets.SetAsync("token", Value, Token);

        await installation.Secrets.SetAsync("token", "replacement-value-0987654321", Token);

        Assert.Equal(2, raised);
        Assert.Equal(SecretMasker.Replacement, masker.Mask("replacement-value-0987654321"));
        Assert.Equal(Value, masker.Mask(Value));
    }

    [Fact]
    public async Task A_name_that_cannot_be_recorded_still_tells_whoever_masks_that_the_file_is_there()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        var masker = new SecretMasker(installation.Secrets);
        // No table, so the name cannot be written after the file is.
        await using (var context = installation.Database.OpenWriter())
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE SecretNames", Token);
        }

        await Assert.ThrowsAnyAsync<Exception>(() => installation.Secrets.SetAsync("token", Value, Token));

        Assert.Equal(SecretMasker.Replacement, masker.Mask(Value));
    }

    [Fact]
    public async Task A_masker_over_the_real_store_follows_every_change_and_survives_a_restart()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        var masker = new SecretMasker(installation.Secrets);
        await masker.RebuildAsync(Token);

        await installation.Secrets.SetAsync("token", Value, Token);
        Assert.Equal($"a {SecretMasker.Replacement} b", masker.Mask($"a {Value} b"));

        using var restarted = installation.Restart();
        var afterRestart = new SecretMasker(restarted.GetRequiredService<ISecretStore>());
        await afterRestart.RebuildAsync(Token);
        Assert.Equal(SecretMasker.Replacement, afterRestart.Mask(Value));

        await installation.Secrets.RemoveAsync("token", Token);
        Assert.Equal(Value, masker.Mask(Value));
    }
}
