using Microsoft.EntityFrameworkCore.Migrations;


namespace Hephaestus.Persistence.Migrations;

/// <summary>
/// Adds the Projects (Story 1.10), identified by GitHub's numeric repository id; the name is a display column. A removed Project
/// keeps its row and has its removal time set.
/// </summary>
internal partial class AddProjects : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Projects",
            columns: table => new
            {
                RepositoryId = table.Column<long>(type: "INTEGER", nullable: false),
                FullName = table.Column<string>(type: "TEXT", maxLength: 140, nullable: false),
                BaseBranch = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                RegisteredAt = table.Column<string>(type: "TEXT", nullable: false),
                RemovedAt = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Projects", x => x.RepositoryId);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Projects");
    }
}
