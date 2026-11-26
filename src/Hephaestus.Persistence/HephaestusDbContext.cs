using Hephaestus.Persistence.Model;
using Microsoft.EntityFrameworkCore;

namespace Hephaestus.Persistence;

/// <summary>The one database context and, with its migrations, the one migration set (AR-35).</summary>
internal sealed class HephaestusDbContext(DbContextOptions<HephaestusDbContext> options) : DbContext(options)
{
    public DbSet<SecretName> SecretNames => Set<SecretName>();

    public DbSet<GitHubAppConnection> GitHubAppConnections => Set<GitHubAppConnection>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configuration)
    {
        configuration.Properties<Guid>().HaveConversion<EntityIdConverter>();
        configuration.Properties<DateTimeOffset>().HaveConversion<UtcInstantConverter>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<SecretName>(secret =>
        {
            secret.ToTable("SecretNames");
            secret.HasKey(s => s.Id);
            secret.Property(s => s.Id).ValueGeneratedNever();
            secret.Property(s => s.Name).IsRequired().HasMaxLength(Core.Secrets.SecretPolicy.MaximumNameLength);
            secret.HasIndex(s => s.Name).IsUnique();
        });

        model.Entity<GitHubAppConnection>(app =>
        {
            app.ToTable("GitHubAppConnections", table => table.HasCheckConstraint("CK_GitHubAppConnections_Single", "\"Id\" = 1"));
            app.HasKey(c => c.Id);
            app.Property(c => c.Id).ValueGeneratedNever();
        });
    }
}
