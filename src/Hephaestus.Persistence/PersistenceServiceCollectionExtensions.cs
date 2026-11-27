using Hephaestus.Core.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hephaestus.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>Registers the storage ports this adapter implements, and the migrator the host runs before it listens.</summary>
    public static IServiceCollection AddHephaestusPersistence(this IServiceCollection services, PersistenceOptions options)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<HephaestusDatabase>();
        services.AddSingleton(provider => new DatabaseMigrator(provider.GetRequiredService<HephaestusDatabase>()));
        services.AddSingleton<IReadStore, SqliteReadStore>();
        services.AddSingleton<ISecretStore, FileSecretStore>();
        services.AddSingleton<IInstallationConfiguration, SqliteInstallationConfiguration>();
        services.AddSingleton<ISignalInbox, SqliteSignalInbox>();
        services.AddSingleton<IPollCursorStore, SqlitePollCursorStore>();
        services.AddSingleton<IUnitOfWork, SqliteUnitOfWork>();
        services.AddSingleton<IOutputStore, SqliteOutputStore>();
        return services;
    }
}
