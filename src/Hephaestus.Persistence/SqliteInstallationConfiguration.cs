using Hephaestus.Core.Ports;
using Hephaestus.Core.Trust;
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

    public async Task<TrustedUserWrite> AddTrustedUserAsync(long userId, string login, CancellationToken cancellationToken)
    {
        await using var context = database.OpenWriter();
        var addedAt = UtcInstantConverter.ToText(time.GetUtcNow());

        // One transaction, so that a removal cannot fall between the writes and the read of the row. The insert is the statement that
        // knows whether the user was new; a user who was not keeps their time and has only the login refreshed.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var inserted = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO "TrustedUsers" ("UserId", "Login", "AddedAt") VALUES ({userId}, {login}, {addedAt}) ON CONFLICT ("UserId") DO NOTHING""",
            cancellationToken);
        if (inserted == 0)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "TrustedUsers" SET "Login" = {login} WHERE "UserId" = {userId}""", cancellationToken);
        }

        var stored = await context.TrustedUsers.AsNoTracking().SingleAsync(u => u.UserId == userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TrustedUserWrite(new TrustedUser(stored.UserId, stored.Login, stored.AddedAt), inserted > 0);
    }

    public async Task<bool> RemoveTrustedUserAsync(long userId, CancellationToken cancellationToken)
    {
        await using var context = database.OpenWriter();
        return await context.TrustedUsers.Where(u => u.UserId == userId).ExecuteDeleteAsync(cancellationToken) > 0;
    }
}
