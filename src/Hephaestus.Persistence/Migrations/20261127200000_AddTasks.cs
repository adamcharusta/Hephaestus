using Microsoft.EntityFrameworkCore.Migrations;


namespace Hephaestus.Persistence.Migrations;

/// <summary>
/// Adds Tasks and the snapshots of their issues (Story 2.2, AD-1, AD-15), and what the loop needs of the inbox: what became of each
/// Signal, why, and which Task it concerned, with the issue's title and body that a labelled event carries. A Signal appended before
/// this migration is pending, so the loop's first tick applies it. The slug of the App is kept beside its id, to know its bot account.
/// </summary>
internal partial class AddTasks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AppSlug",
            table: "GitHubAppConnections",
            type: "TEXT",
            maxLength: 39,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "IssueBody",
            table: "Signals",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "IssueTitle",
            table: "Signals",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ProcessedAt",
            table: "Signals",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Reason",
            table: "Signals",
            type: "TEXT",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Status",
            table: "Signals",
            type: "TEXT",
            maxLength: 16,
            nullable: false,
            defaultValue: "pending");

        migrationBuilder.AddColumn<string>(
            name: "TaskId",
            table: "Signals",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "IssueSnapshots",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                TaskId = table.Column<string>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", nullable: false),
                Body = table.Column<string>(type: "TEXT", nullable: false),
                Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                SignalSequence = table.Column<long>(type: "INTEGER", nullable: false),
                TakenAt = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IssueSnapshots", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Tasks",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                RepositoryId = table.Column<long>(type: "INTEGER", nullable: false),
                IssueNumber = table.Column<int>(type: "INTEGER", nullable: false),
                State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Branch = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Tasks", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Signals_Status_Sequence",
            table: "Signals",
            columns: new[] { "Status", "Sequence" });

        migrationBuilder.CreateIndex(
            name: "IX_IssueSnapshots_TaskId",
            table: "IssueSnapshots",
            column: "TaskId");

        migrationBuilder.CreateIndex(
            name: "IX_Tasks_RepositoryId_IssueNumber",
            table: "Tasks",
            columns: new[] { "RepositoryId", "IssueNumber" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "IssueSnapshots");

        migrationBuilder.DropTable(
            name: "Tasks");

        migrationBuilder.DropIndex(
            name: "IX_Signals_Status_Sequence",
            table: "Signals");

        migrationBuilder.DropColumn(
            name: "AppSlug",
            table: "GitHubAppConnections");

        migrationBuilder.DropColumn(
            name: "IssueBody",
            table: "Signals");

        migrationBuilder.DropColumn(
            name: "IssueTitle",
            table: "Signals");

        migrationBuilder.DropColumn(
            name: "ProcessedAt",
            table: "Signals");

        migrationBuilder.DropColumn(
            name: "Reason",
            table: "Signals");

        migrationBuilder.DropColumn(
            name: "Status",
            table: "Signals");

        migrationBuilder.DropColumn(
            name: "TaskId",
            table: "Signals");
    }
}
