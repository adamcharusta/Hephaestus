using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hephaestus.Persistence;

/// <summary>Sets the per-connection SQLite settings each time a connection opens: a busy timeout, and foreign keys on.</summary>
internal sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private static readonly string Pragmas = $"PRAGMA busy_timeout = {PersistenceOptions.BusyTimeoutMilliseconds}; PRAGMA foreign_keys = ON;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
