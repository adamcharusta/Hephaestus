using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hephaestus.Persistence;

/// <summary>
/// Opens the one database two ways: a writer for the code that changes it, and a read-only connection for <c>IReadStore</c>
/// (AD-17), so that a read cannot change anything.
/// </summary>
internal sealed class HephaestusDatabase(PersistenceOptions options)
{
    private readonly DbContextOptions<HephaestusDbContext> _writer = Build(options.DatabasePath, SqliteOpenMode.ReadWriteCreate, readOnly: false);
    private readonly DbContextOptions<HephaestusDbContext> _reader = Build(options.DatabasePath, SqliteOpenMode.ReadOnly, readOnly: true);

    public PersistenceOptions Options => options;

    public HephaestusDbContext OpenWriter() => new(_writer);

    public HephaestusDbContext OpenReader() => new(_reader);

    private static DbContextOptions<HephaestusDbContext> Build(string path, SqliteOpenMode mode, bool readOnly)
    {
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
            DefaultTimeout = PersistenceOptions.BusyTimeoutMilliseconds / 1000,
        }.ToString();

        var builder = new DbContextOptionsBuilder<HephaestusDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new SqlitePragmaInterceptor());
        if (readOnly)
        {
            builder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }

        return builder.Options;
    }
}
