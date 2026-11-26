using Hephaestus.Core.Ports;
using Hephaestus.Persistence.Model;
using Microsoft.EntityFrameworkCore;

namespace Hephaestus.Persistence;

/// <summary><see cref="IInstallationConfiguration"/>: each write is its own transaction on the writer connection (AD-16).</summary>
internal sealed class SqliteInstallationConfiguration(HephaestusDatabase database, TimeProvider time) : IInstallationConfiguration
{
    public async Task SetGitHubAppIdAsync(long appId, CancellationToken cancellationToken)
    {
        await using var context = database.OpenWriter();
        var connectedAt = UtcInstantConverter.ToText(time.GetUtcNow());

        // One statement, so that two writers cannot both find no row and both insert it.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "GitHubAppConnections" ("Id", "AppId", "ConnectedAt") VALUES ({GitHubAppConnection.TheOnlyId}, {appId}, {connectedAt})
            ON CONFLICT ("Id") DO UPDATE SET "AppId" = excluded."AppId", "ConnectedAt" = excluded."ConnectedAt"
            """,
            cancellationToken);
    }
}
