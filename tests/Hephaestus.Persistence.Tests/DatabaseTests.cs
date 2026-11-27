using Microsoft.EntityFrameworkCore;

namespace Hephaestus.Persistence.Tests;

/// <summary>A first start (Story 1.5, AR-35): one SQLite database in write-ahead-log mode with a busy timeout, and the migrations of the single set.</summary>
public class DatabaseTests
{
    private static async Task<string> ScalarAsync(HephaestusDbContext context, string sql)
    {
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    [Fact]
    public async Task A_first_start_creates_the_database_on_the_data_volume_and_applies_the_migrations()
    {
        using var installation = new TempInstallation();
        Assert.False(Directory.Exists(installation.Options.DataDirectory));

        await installation.MigrateAsync();

        Assert.True(File.Exists(installation.Options.DatabasePath));
        Assert.Equal(PersistenceOptions.DatabaseFileName, Path.GetFileName(installation.Options.DatabasePath));
        await using var context = installation.Database.OpenWriter();
        Assert.Equal(["20261126194024_InitialCreate", "20261126220000_AddGitHubAppConnection", "20261126230000_AddTrustedUsers", "20261127000000_AddInstallationConfiguration", "20261127010000_AddProjects", "20261127100000_AddSignalInbox", "20261127200000_AddTasks", "20261127300000_AddEffectJournal", "20261127400000_AddIgnoredLabelNotices", "20261127500000_AddEscalations", "20261127600000_AddAdmittedComments", "20261127700000_AddOutputFiles", "20261127800000_AddRefusedHosts"], await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_database_is_in_write_ahead_log_mode_for_the_writer_and_the_read_only_connection()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        await using var writer = installation.Database.OpenWriter();
        await using var reader = installation.Database.OpenReader();

        Assert.Equal("wal", await ScalarAsync(writer, "PRAGMA journal_mode;"));
        Assert.Equal("wal", await ScalarAsync(reader, "PRAGMA journal_mode;"));
    }

    [Fact]
    public async Task The_database_file_header_says_write_ahead_log_so_a_tool_without_sqlite_can_see_it()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        await using var stream = new FileStream(installation.Options.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var header = new byte[20];
        await stream.ReadExactlyAsync(header, TestContext.Current.CancellationToken);

        Assert.Equal([2, 2], header[18..20]);
    }

    [Fact]
    public async Task Every_connection_has_a_busy_timeout_and_foreign_keys_on()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        await using var writer = installation.Database.OpenWriter();
        await using var reader = installation.Database.OpenReader();

        Assert.Equal("5000", await ScalarAsync(writer, "PRAGMA busy_timeout;"));
        Assert.Equal("5000", await ScalarAsync(reader, "PRAGMA busy_timeout;"));
        Assert.Equal("1", await ScalarAsync(writer, "PRAGMA foreign_keys;"));
    }

    [Fact]
    public async Task Migrating_again_changes_nothing()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();
        await installation.MigrateAsync();

        await using var context = installation.Database.OpenWriter();
        Assert.Equal(13, (await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Count());
    }

    [Fact]
    public async Task The_migration_set_matches_the_model()
    {
        using var installation = new TempInstallation();

        await using var context = installation.Database.OpenWriter();

        Assert.False(context.Database.HasPendingModelChanges(), "The model changed without a migration; add one to the single migration set.");
    }

    [Fact]
    public async Task The_read_only_connection_cannot_change_the_database()
    {
        using var installation = new TempInstallation();
        await installation.MigrateAsync();

        await using var reader = installation.Database.OpenReader();

        await Assert.ThrowsAnyAsync<Exception>(() => reader.Database.ExecuteSqlRawAsync("DELETE FROM SecretNames", TestContext.Current.CancellationToken));
    }
}
