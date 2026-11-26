using Hephaestus.Core.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace Hephaestus.Persistence.Tests;

/// <summary>A data directory and a secrets directory of their own, removed with the object, and the adapter's services over them.</summary>
internal sealed class TempInstallation : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("hxp").FullName;

    public TempInstallation(TimeProvider? time = null)
    {
        Options = new PersistenceOptions(Path.Combine(_directory, "data"), Path.Combine(_directory, "secrets"));
        var services = new ServiceCollection();
        if (time is not null)
        {
            services.AddSingleton(time);
        }

        Provider = services.AddHephaestusPersistence(Options).BuildServiceProvider();
    }

    public PersistenceOptions Options { get; }

    public ServiceProvider Provider { get; }

    public ISecretStore Secrets => Provider.GetRequiredService<ISecretStore>();

    public IReadStore Reads => Provider.GetRequiredService<IReadStore>();

    public HephaestusDatabase Database => Provider.GetRequiredService<HephaestusDatabase>();

    public Task MigrateAsync() => Provider.GetRequiredService<DatabaseMigrator>().MigrateAsync(TestContext.Current.CancellationToken);

    /// <summary>The same directories under a second set of services, as a restarted host sees them.</summary>
    public ServiceProvider Restart() => new ServiceCollection().AddHephaestusPersistence(Options).BuildServiceProvider();

    public IEnumerable<string> FilesOfTheDatabase() => Directory.EnumerateFiles(Options.DataDirectory);

    public void Dispose()
    {
        Provider.Dispose();
        Directory.Delete(_directory, recursive: true);
    }
}

/// <summary>A clock that stands where a test puts it.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
