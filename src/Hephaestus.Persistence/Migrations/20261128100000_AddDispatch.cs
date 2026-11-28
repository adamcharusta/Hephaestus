using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hephaestus.Persistence.Migrations;

/// <summary>
/// Records what starting a Task needs (Story 3.14, AD-1): the windows of the Projects, at most one open for each; the step runs of the Tasks; and the snapshot each Task started with. The database also refuses a second Task in
/// the state <c>running</c>, so that the rule that two Tasks never run does not rest on the loop alone. Nothing that exists is changed.
/// </summary>
internal partial class AddDispatch : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "StepRuns",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                TaskId = table.Column<string>(type: "TEXT", nullable: false),
                Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Round = table.Column<int>(type: "INTEGER", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Deadline = table.Column<string>(type: "TEXT", nullable: false),
                PlannedAt = table.Column<string>(type: "TEXT", nullable: false),
                StartedAt = table.Column<string>(type: "TEXT", nullable: true),
                EndedAt = table.Column<string>(type: "TEXT", nullable: true),
                Agent = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                Effort = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                Methodology = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                Template = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                CommandIndex = table.Column<int>(type: "INTEGER", nullable: true),
                ExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                Cause = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                Failure = table.Column<string>(type: "TEXT", maxLength: 24, nullable: true),
                Outcome = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                Result = table.Column<string>(type: "TEXT", nullable: true),
                TipBefore = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                TipAfter = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StepRuns", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "TaskSnapshots",
            columns: table => new
            {
                TaskId = table.Column<string>(type: "TEXT", nullable: false),
                CommitId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                ImageDigest = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Document = table.Column<string>(type: "TEXT", nullable: false),
                TakenAt = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TaskSnapshots", x => x.TaskId);
            });

        migrationBuilder.CreateTable(
            name: "Windows",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                RepositoryId = table.Column<long>(type: "INTEGER", nullable: false),
                OpenedAt = table.Column<string>(type: "TEXT", nullable: false),
                ClosedAt = table.Column<string>(type: "TEXT", nullable: true),
                End = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Windows", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Tasks_Running",
            table: "Tasks",
            column: "State",
            unique: true,
            filter: "\"State\" = 'running'");

        migrationBuilder.CreateIndex(
            name: "IX_StepRuns_TaskId_Sequence",
            table: "StepRuns",
            columns: new[] { "TaskId", "Sequence" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Windows_Open",
            table: "Windows",
            column: "RepositoryId",
            unique: true,
            filter: "\"ClosedAt\" IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "StepRuns");

        migrationBuilder.DropTable(
            name: "TaskSnapshots");

        migrationBuilder.DropTable(
            name: "Windows");

        migrationBuilder.DropIndex(
            name: "IX_Tasks_Running",
            table: "Tasks");
    }
}
