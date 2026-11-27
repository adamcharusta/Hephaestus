using Hephaestus.Core.Ports;
using Microsoft.EntityFrameworkCore;

namespace Hephaestus.Persistence;

/// <summary><see cref="IPollCursorStore"/> over the <c>PollCursors</c> table (AD-1): one row per Project and stream.</summary>
internal sealed class SqlitePollCursorStore(HephaestusDatabase database) : IPollCursorStore
{
    public async Task<PollCursor?> GetAsync(long repositoryId, string stream, CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        var stored = await context.PollCursors.FirstOrDefaultAsync(c => c.RepositoryId == repositoryId && c.Stream == stream, cancellationToken);
        return stored is null ? null : new PollCursor(stored.Position, stored.ETag);
    }

    public async Task SetAsync(long repositoryId, string stream, PollCursor cursor, CancellationToken cancellationToken)
    {
        await using var context = database.OpenWriter();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "PollCursors" ("RepositoryId", "Stream", "Position", "ETag") VALUES ({repositoryId}, {stream}, {cursor.Position}, {cursor.ETag})
            ON CONFLICT ("RepositoryId", "Stream") DO UPDATE SET "Position" = excluded."Position", "ETag" = excluded."ETag"
            """,
            cancellationToken);
    }
}
