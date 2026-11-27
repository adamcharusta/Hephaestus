using Microsoft.EntityFrameworkCore.Migrations;

namespace Hephaestus.Persistence.Migrations;

/// <summary>
/// Keeps what a step printed and reports how it ended (Story 3.3, FR-53, AD-18): the index of the output file of each container run, and the
/// columns that a step-exited Signal fills in the inbox. Nothing that exists is changed.
/// </summary>
internal partial class AddOutputFiles : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "StepRunId", table: "Signals", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<int>(name: "ExitCode", table: "Signals", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<string>(name: "Cause", table: "Signals", type: "TEXT", maxLength: 16, nullable: true);

        migrationBuilder.CreateTable(
            name: "OutputFiles",
            columns: table => new
            {
                StepRunId = table.Column<string>(type: "TEXT", nullable: false),
                TaskId = table.Column<string>(type: "TEXT", nullable: false),
                ContainerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Path = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Bytes = table.Column<long>(type: "INTEGER", nullable: false),
                StartedAt = table.Column<string>(type: "TEXT", nullable: false),
                CompletedAt = table.Column<string>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OutputFiles", x => x.StepRunId);
            });

        migrationBuilder.CreateIndex(name: "IX_OutputFiles_TaskId", table: "OutputFiles", column: "TaskId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "OutputFiles");

        migrationBuilder.DropColumn(name: "Cause", table: "Signals");
        migrationBuilder.DropColumn(name: "ExitCode", table: "Signals");
        migrationBuilder.DropColumn(name: "StepRunId", table: "Signals");
    }
}
