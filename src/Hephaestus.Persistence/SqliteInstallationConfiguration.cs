using Hephaestus.Core.Aliases;
using Hephaestus.Core.Limits;
using Hephaestus.Core.Ports;
using Hephaestus.Core.Projects;
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

    public async Task<AliasWrite> AddAliasAsync(Alias alias, CancellationToken cancellationToken)
    {
        await using var context = database.OpenWriter();
        var addedAt = UtcInstantConverter.ToText(alias.AddedAt);
        var agent = alias.Agent?.Tag();
        var effort = alias.Effort?.Tag();

        // One transaction: the insert is the statement that knows whether the name was free, and the row read back is the one now stored.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var inserted = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "Aliases" ("Name", "Agent", "Model", "Effort", "AddedAt") VALUES ({alias.Name}, {agent}, {alias.Model}, {effort}, {addedAt})
            ON CONFLICT ("Name") DO NOTHING
            """,
            cancellationToken);
        var stored = await context.Aliases.AsNoTracking().SingleAsync(a => a.Name == alias.Name, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AliasWrite(SqliteReadStore.ToAlias(stored), inserted > 0);
    }

    public async Task<Alias?> RemoveAliasAsync(string name, CancellationToken cancellationToken)
    {
        await using var context = database.OpenWriter();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // A write first: a transaction that reads and then writes fails at once, past the busy timeout, when another writer has
        // committed in between, and this one must answer "not an Alias" or the Alias, never an error.
        var touched = await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "Aliases" SET "Name" = "Name" WHERE "Name" = {name}""", cancellationToken);
        if (touched == 0)
        {
            return null;
        }

        var stored = await context.Aliases.AsNoTracking().SingleAsync(a => a.Name == name, cancellationToken);
        await context.Aliases.Where(a => a.Name == name).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return SqliteReadStore.ToAlias(stored);
    }

    public async Task<InstallationLimits> SetLimitsAsync(LimitsChange change, CancellationToken cancellationToken)
    {
        await using var context = database.OpenWriter();
        var seconds = change.TaskDuration is { } duration ? (long?)duration.TotalSeconds : null;

        // One statement per change keeps the limits that were not named; the row read back is what is stored now.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "InstallationLimits" ("Id", "TaskDurationSeconds", "CpuMillis", "MemoryBytes")
            VALUES ({InstallationLimitsEntry.TheOnlyId}, {seconds}, {change.CpuMillis}, {change.MemoryBytes})
            ON CONFLICT ("Id") DO UPDATE SET
                "TaskDurationSeconds" = COALESCE(excluded."TaskDurationSeconds", "InstallationLimits"."TaskDurationSeconds"),
                "CpuMillis" = COALESCE(excluded."CpuMillis", "InstallationLimits"."CpuMillis"),
                "MemoryBytes" = COALESCE(excluded."MemoryBytes", "InstallationLimits"."MemoryBytes")
            """,
            cancellationToken);
        var stored = await context.InstallationLimits.AsNoTracking().SingleAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return SqliteReadStore.ToLimits(stored);
    }

    public async Task<bool> AddProtectedPathAsync(string glob, CancellationToken cancellationToken)
    {
        await using var context = database.OpenWriter();
        return await context.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO "ProtectedPaths" ("Pattern") VALUES ({glob}) ON CONFLICT ("Pattern") DO NOTHING""",
            cancellationToken) > 0;
    }

    public async Task<bool> RemoveProtectedPathAsync(string glob, CancellationToken cancellationToken)
    {
        await using var context = database.OpenWriter();
        return await context.ProtectedPaths.Where(p => p.Pattern == glob).ExecuteDeleteAsync(cancellationToken) > 0;
    }

    public async Task SetSettingAsync(string name, string value, CancellationToken cancellationToken)
    {
        await using var context = database.OpenWriter();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "Settings" ("Name", "Value") VALUES ({name}, {value})
            ON CONFLICT ("Name") DO UPDATE SET "Value" = excluded."Value"
            """,
            cancellationToken);
    }

    public async Task<ProjectWrite> RegisterProjectAsync(long repositoryId, string fullName, string baseBranch, CancellationToken cancellationToken)
    {
        await using var context = database.OpenWriter();
        var registeredAt = UtcInstantConverter.ToText(time.GetUtcNow());
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // A write first, so that the transaction holds the lock before it reads what the row was: a transaction that reads and then
        // writes fails at once, past the busy timeout, when another writer has committed in between.
        var touched = await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "Projects" SET "FullName" = "FullName" WHERE "RepositoryId" = {repositoryId}""", cancellationToken);
        var previous = touched == 0 ? null : await context.Projects.AsNoTracking().SingleAsync(p => p.RepositoryId == repositoryId, cancellationToken);
        var change = previous is null || previous.RemovedAt is not null
            ? ProjectChange.Registered
            : previous.FullName == fullName && previous.BaseBranch == baseBranch ? ProjectChange.Unchanged : ProjectChange.Updated;
        if (change != ProjectChange.Unchanged)
        {
            // The id and the time of the first registration are kept; a removed Project is registered again from its record.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "Projects" ("RepositoryId", "FullName", "BaseBranch", "RegisteredAt", "RemovedAt") VALUES ({repositoryId}, {fullName}, {baseBranch}, {registeredAt}, NULL)
                ON CONFLICT ("RepositoryId") DO UPDATE SET "FullName" = excluded."FullName", "BaseBranch" = excluded."BaseBranch", "RemovedAt" = NULL
                """,
                cancellationToken);
        }

        var stored = await context.Projects.AsNoTracking().SingleAsync(p => p.RepositoryId == repositoryId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ProjectWrite(SqliteReadStore.ToProject(stored), change);
    }

    public async Task<Project?> RemoveProjectAsync(long repositoryId, CancellationToken cancellationToken)
    {
        await using var context = database.OpenWriter();
        var removedAt = UtcInstantConverter.ToText(time.GetUtcNow());
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // The row stays: only the time of its removal is written, so that everything recorded under the Project is retained.
        var removed = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Projects" SET "RemovedAt" = {removedAt} WHERE "RepositoryId" = {repositoryId} AND "RemovedAt" IS NULL""",
            cancellationToken);
        if (removed == 0)
        {
            return null;
        }

        var stored = await context.Projects.AsNoTracking().SingleAsync(p => p.RepositoryId == repositoryId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return SqliteReadStore.ToProject(stored);
    }
}
