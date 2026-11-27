using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Persistence.Tests;

/// <summary>Story 1.7, AD-4: the id of Hephaestus's own GitHub App is Installation Configuration, one row, read through the read store.</summary>
public class GitHubAppConnectionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_App_is_connected_until_an_id_is_recorded()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        Assert.Null(await installation.Reads.GetGitHubAppIdAsync(Token));
    }

    [Fact]
    public async Task A_recorded_id_is_read_back_and_a_later_one_replaces_it()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        await installation.Configuration.SetGitHubAppIdAsync(1_234_567, "hephaestus-test", Token);
        Assert.Equal(1_234_567, await installation.Reads.GetGitHubAppIdAsync(Token));

        await installation.Configuration.SetGitHubAppIdAsync(7_654_321, "hephaestus-test", Token);
        Assert.Equal(7_654_321, await installation.Reads.GetGitHubAppIdAsync(Token));
        await using var context = installation.Database.OpenReader();
        Assert.Equal(1, await context.GitHubAppConnections.CountAsync(Token));
    }

    [Fact]
    public async Task Many_writers_at_once_leave_one_row_and_none_fails()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        await Task.WhenAll(Enumerable.Range(1, 20).Select(id => installation.Configuration.SetGitHubAppIdAsync(id, "hephaestus-test", Token)));

        await using var context = installation.Database.OpenReader();
        Assert.Equal(1, await context.GitHubAppConnections.CountAsync(Token));
        Assert.InRange(await installation.Reads.GetGitHubAppIdAsync(Token) ?? 0, 1, 20);
    }

    [Fact]
    public async Task The_id_survives_a_restart()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        await installation.Configuration.SetGitHubAppIdAsync(42, "hephaestus-test", Token);

        await using var restarted = installation.Restart();

        Assert.Equal(42, await restarted.GetRequiredService<Hephaestus.Core.Ports.IReadStore>().GetGitHubAppIdAsync(Token));
    }

    [Fact]
    public async Task The_model_and_the_migrations_agree()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        await using var context = installation.Database.OpenWriter();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task A_second_row_is_refused_by_the_database()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        await installation.Configuration.SetGitHubAppIdAsync(42, "hephaestus-test", Token);

        await using var context = installation.Database.OpenWriter();
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            context.GitHubAppConnections.Add(new Model.GitHubAppConnection { Id = 2, AppId = 43, ConnectedAt = DateTimeOffset.UnixEpoch });
            await context.SaveChangesAsync(Token);
        });
    }
}
