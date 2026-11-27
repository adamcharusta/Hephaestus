using Hephaestus.Persistence.Model;
using Microsoft.EntityFrameworkCore;

namespace Hephaestus.Persistence;

/// <summary>The one database context and, with its migrations, the one migration set (AR-35).</summary>
internal sealed class HephaestusDbContext(DbContextOptions<HephaestusDbContext> options) : DbContext(options)
{
    public DbSet<SecretName> SecretNames => Set<SecretName>();

    public DbSet<GitHubAppConnection> GitHubAppConnections => Set<GitHubAppConnection>();

    public DbSet<TrustedUserEntry> TrustedUsers => Set<TrustedUserEntry>();

    public DbSet<AliasEntry> Aliases => Set<AliasEntry>();

    public DbSet<InstallationLimitsEntry> InstallationLimits => Set<InstallationLimitsEntry>();

    public DbSet<ProtectedPathEntry> ProtectedPaths => Set<ProtectedPathEntry>();

    public DbSet<SettingEntry> Settings => Set<SettingEntry>();

    public DbSet<ProjectEntry> Projects => Set<ProjectEntry>();

    public DbSet<SignalEntry> Signals => Set<SignalEntry>();

    public DbSet<PollCursorEntry> PollCursors => Set<PollCursorEntry>();

    public DbSet<TaskEntry> Tasks => Set<TaskEntry>();

    public DbSet<IssueSnapshotEntry> IssueSnapshots => Set<IssueSnapshotEntry>();

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
            app.Property(c => c.AppSlug).HasMaxLength(Core.Trust.GitHubLogin.MaximumLength);
        });

        model.Entity<TrustedUserEntry>(user =>
        {
            user.ToTable("TrustedUsers");
            user.HasKey(u => u.UserId);
            user.Property(u => u.UserId).ValueGeneratedNever();
            user.Property(u => u.Login).IsRequired().HasMaxLength(Core.Trust.GitHubLogin.MaximumLength + Core.Trust.GitHubLogin.BotSuffix.Length);
        });

        model.Entity<AliasEntry>(alias =>
        {
            alias.ToTable("Aliases", table => table.HasCheckConstraint(
                "CK_Aliases_AgentWithModelOrEffort",
                "(\"Agent\" IS NOT NULL AND \"Model\" IS NOT NULL AND \"Effort\" IS NULL) OR (\"Agent\" IS NULL AND \"Model\" IS NULL AND \"Effort\" IS NOT NULL)"));
            alias.HasKey(a => a.Name);
            alias.Property(a => a.Name).HasMaxLength(Core.Aliases.AliasShape.MaximumNameLength).ValueGeneratedNever();
            alias.Property(a => a.Agent).HasMaxLength(16);
            alias.Property(a => a.Model).HasMaxLength(Core.Aliases.AliasShape.MaximumModelLength);
            alias.Property(a => a.Effort).HasMaxLength(8);
        });

        model.Entity<InstallationLimitsEntry>(limits =>
        {
            limits.ToTable("InstallationLimits", table => table.HasCheckConstraint("CK_InstallationLimits_Single", "\"Id\" = 1"));
            limits.HasKey(l => l.Id);
            limits.Property(l => l.Id).ValueGeneratedNever();
        });

        model.Entity<ProtectedPathEntry>(path =>
        {
            path.ToTable("ProtectedPaths");
            path.HasKey(p => p.Pattern);
            path.Property(p => p.Pattern).HasMaxLength(Core.Protection.ProtectedPath.MaximumLength).ValueGeneratedNever();
        });

        model.Entity<SettingEntry>(setting =>
        {
            setting.ToTable("Settings");
            setting.HasKey(s => s.Name);
            setting.Property(s => s.Name).HasMaxLength(64).ValueGeneratedNever();
            setting.Property(s => s.Value).IsRequired().HasMaxLength(200);
        });

        model.Entity<ProjectEntry>(project =>
        {
            project.ToTable("Projects");
            project.HasKey(p => p.RepositoryId);
            project.Property(p => p.RepositoryId).ValueGeneratedNever();
            project.Property(p => p.FullName).IsRequired().HasMaxLength(Core.Projects.ProjectName.MaximumOwnerLength + 1 + Core.Projects.ProjectName.MaximumRepositoryLength);
            project.Property(p => p.BaseBranch).IsRequired().HasMaxLength(Core.Projects.ProjectName.MaximumBranchLength);
        });

        model.Entity<SignalEntry>(signal =>
        {
            signal.ToTable("Signals");
            signal.HasKey(s => s.Sequence);
            signal.Property(s => s.Sequence).ValueGeneratedOnAdd();
            signal.Property(s => s.Source).IsRequired().HasMaxLength(32);
            signal.Property(s => s.SourceEventId).IsRequired().HasMaxLength(128);
            signal.Property(s => s.Kind).IsRequired().HasMaxLength(32);
            signal.Property(s => s.ActorLogin).HasMaxLength(Core.Trust.GitHubLogin.MaximumLength + Core.Trust.GitHubLogin.BotSuffix.Length);
            signal.Property(s => s.Label).HasMaxLength(100);
            signal.Property(s => s.Status).IsRequired().HasMaxLength(16).HasDefaultValue("pending");
            signal.Property(s => s.Reason).HasMaxLength(32);
            signal.HasIndex(s => new { s.Source, s.SourceEventId }).IsUnique();
            signal.HasIndex(s => new { s.Status, s.Sequence });
        });

        model.Entity<TaskEntry>(task =>
        {
            task.ToTable("Tasks");
            task.HasKey(t => t.Id);
            task.Property(t => t.Id).ValueGeneratedNever();
            task.Property(t => t.State).IsRequired().HasMaxLength(32);
            task.Property(t => t.Branch).IsRequired().HasMaxLength(80);
            task.HasIndex(t => new { t.RepositoryId, t.IssueNumber }).IsUnique();
        });

        model.Entity<IssueSnapshotEntry>(snapshot =>
        {
            snapshot.ToTable("IssueSnapshots");
            snapshot.HasKey(s => s.Id);
            snapshot.Property(s => s.Id).ValueGeneratedOnAdd();
            snapshot.Property(s => s.Title).IsRequired();
            snapshot.Property(s => s.Body).IsRequired();
            snapshot.Property(s => s.Hash).IsRequired().HasMaxLength(64);
            snapshot.HasIndex(s => s.TaskId);
        });

        model.Entity<PollCursorEntry>(cursor =>
        {
            cursor.ToTable("PollCursors");
            cursor.HasKey(c => new { c.RepositoryId, c.Stream });
            cursor.Property(c => c.Stream).IsRequired().HasMaxLength(32);
            cursor.Property(c => c.Position).IsRequired().HasMaxLength(64);
            cursor.Property(c => c.ETag).HasMaxLength(256);
        });
    }
}
