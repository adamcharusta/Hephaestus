using Microsoft.EntityFrameworkCore;

namespace Hephaestus.Persistence;

/// <summary>
/// Creates the SQLite database on first start, puts it in write-ahead-log mode, and applies the migrations of the single
/// migration set (AR-35). The host calls it before it listens.
/// </summary>
public sealed class DatabaseMigrator
{
    private readonly HephaestusDatabase _database;

    internal DatabaseMigrator(HephaestusDatabase database) => _database = database;

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(_database.Options.DataDirectory);
        }
        else
        {
            // A directory this creates is for its owner alone; one the volume brings keeps the mode the image gave it.
            Directory.CreateDirectory(_database.Options.DataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        await using var context = _database.OpenWriter();
        await context.Database.OpenConnectionAsync(cancellationToken);

        // The journal mode is stored in the database file, so one start sets it for every later connection, the read-only one too.
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL;";
        var mode = (string?)await command.ExecuteScalarAsync(cancellationToken);
        if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SQLite did not enter write-ahead-log mode on {_database.Options.DatabasePath}; it reports '{mode}'.");
        }

        await context.Database.MigrateAsync(cancellationToken);
    }
}
