using Hephaestus.Core.Ports;
using Hephaestus.Core.Trust;
using Microsoft.EntityFrameworkCore;

namespace Hephaestus.Persistence;

/// <summary><see cref="IReadStore"/> on a read-only SQLite connection (AD-17).</summary>
internal sealed class SqliteReadStore(HephaestusDatabase database) : IReadStore
{
    public async Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        return await context.SecretNames.OrderBy(s => s.Name).Select(s => s.Name).ToListAsync(cancellationToken);
    }

    public async Task<long?> GetGitHubAppIdAsync(CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        return await context.GitHubAppConnections.Select(c => (long?)c.AppId).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TrustedUser>> ListTrustedUsersAsync(CancellationToken cancellationToken)
    {
        await using var context = database.OpenReader();
        var entries = await context.TrustedUsers.ToListAsync(cancellationToken);

        // The case-insensitive order is the Owner's, not SQLite's binary one.
        return [.. entries
            .OrderBy(u => u.Login, StringComparer.OrdinalIgnoreCase).ThenBy(u => u.Login, StringComparer.Ordinal).ThenBy(u => u.UserId)
            .Select(u => new TrustedUser(u.UserId, u.Login, u.AddedAt))];
    }
}
