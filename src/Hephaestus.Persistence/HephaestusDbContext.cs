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

    public DbSet<EffectJournalEntryEntity> EffectJournal => Set<EffectJournalEntryEntity>();

    public DbSet<EscalationEntry> Escalations => Set<EscalationEntry>();

    public DbSet<AdmittedCommentEntry> AdmittedComments => Set<AdmittedCommentEntry>();

    public DbSet<OutputFileEntry> OutputFiles => Set<OutputFileEntry>();

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
            signal.Property(s => s.Cause).HasMaxLength(16);
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

        model.Entity<AdmittedCommentEntry>(comment =>
        {
            comment.ToTable("AdmittedComments");
            comment.HasKey(c => c.Id);
            comment.Property(c => c.Id).ValueGeneratedOnAdd();
            comment.Property(c => c.AuthorLogin).HasMaxLength(Core.Trust.GitHubLogin.MaximumLength + Core.Trust.GitHubLogin.BotSuffix.Length);
            comment.Property(c => c.Body).IsRequired();
            comment.Property(c => c.Hash).IsRequired().HasMaxLength(64);

            // A comment is admitted once for a Task; the database refuses a second.
            comment.HasIndex(c => new { c.TaskId, c.CommentId }).IsUnique();
        });

        model.Entity<OutputFileEntry>(output =>
        {
            output.ToTable("OutputFiles");
            output.HasKey(o => o.StepRunId);
            output.Property(o => o.StepRunId).ValueGeneratedNever();
            output.Property(o => o.ContainerId).IsRequired().HasMaxLength(128);
            output.Property(o => o.Path).IsRequired().HasMaxLength(200);
            output.HasIndex(o => o.TaskId);
        });

        model.Entity<EffectJournalEntryEntity>(entry =>
        {
            entry.ToTable("EffectJournal");
            entry.HasKey(e => e.Sequence);
            entry.Property(e => e.Sequence).ValueGeneratedOnAdd();
            entry.Property(e => e.Key).IsRequired().HasMaxLength(128);
            entry.Property(e => e.Entry).IsRequired().HasMaxLength(8);
            entry.Property(e => e.Cause).IsRequired().HasMaxLength(32);
            entry.Property(e => e.Kind).IsRequired().HasMaxLength(16);
            entry.Property(e => e.Detail).IsRequired().HasMaxLength(256);
            entry.Property(e => e.NoticeKey).HasMaxLength(64);
            entry.Property(e => e.Result).HasMaxLength(16);
            entry.Property(e => e.Reason).HasMaxLength(32);

            // An effect has one intent and at most one result; the database refuses a second of either.
            entry.HasIndex(e => e.Key).IsUnique().HasFilter("\"Entry\" = 'intent'").HasDatabaseName("IX_EffectJournal_Key_Intent");
            entry.HasIndex(e => e.Key).IsUnique().HasFilter("\"Entry\" = 'result'").HasDatabaseName("IX_EffectJournal_Key_Result");
            entry.HasIndex(e => new { e.Key, e.Sequence });
            entry.HasIndex(e => new { e.TaskId, e.Sequence });

            // What is said on an issue to one user about one kind of action is said once; the database refuses a second intent that would.
            entry.HasIndex(e => new { e.RepositoryId, e.IssueNumber, e.NoticeKey }).IsUnique().HasFilter("\"Entry\" = 'intent' AND \"NoticeKey\" IS NOT NULL").HasDatabaseName("IX_EffectJournal_Notice");
        });

        model.Entity<EscalationEntry>(escalation =>
        {
            escalation.ToTable("Escalations");
            escalation.HasKey(e => e.Id);
            escalation.Property(e => e.Id).ValueGeneratedNever();
            escalation.Property(e => e.Kind).IsRequired().HasMaxLength(16);
            escalation.Property(e => e.Decision).IsRequired();
            escalation.Property(e => e.Options).IsRequired();
            escalation.Property(e => e.ResumeCondition).IsRequired().HasMaxLength(32);
            escalation.Property(e => e.Cause).IsRequired().HasMaxLength(32);
            escalation.Property(e => e.EffectKey).HasMaxLength(128);
            escalation.Property(e => e.EffectKind).HasMaxLength(16);
            escalation.Property(e => e.EffectDetail).HasMaxLength(256);
            escalation.Property(e => e.Check).HasMaxLength(16);
            escalation.Property(e => e.EffectReason).HasMaxLength(32);
            escalation.Property(e => e.Outcome).HasMaxLength(16);
            escalation.HasIndex(e => new { e.TaskId, e.RaisedAt });

            // One Escalation asks for one decision: a Task has at most one that is open, and the database refuses a second.
            escalation.HasIndex(e => e.TaskId).IsUnique().HasFilter("\"ClosedAt\" IS NULL").HasDatabaseName("IX_Escalations_Open");
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
