using Microsoft.EntityFrameworkCore.Migrations;


namespace Hephaestus.Persistence.Migrations;

/// <summary>
/// Adds the rest of the Installation Configuration of Story 1.9: Aliases, the limits, the protected paths, and the settings. A new
/// installation's protected paths are put there by this migration (AD-19); everything else starts empty.
/// </summary>
internal partial class AddInstallationConfiguration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Aliases",
            columns: table => new
            {
                Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Agent = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                Effort = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                AddedAt = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Aliases", x => x.Name);
                table.CheckConstraint("CK_Aliases_AgentWithModelOrEffort", "(\"Agent\" IS NOT NULL AND \"Model\" IS NOT NULL AND \"Effort\" IS NULL) OR (\"Agent\" IS NULL AND \"Model\" IS NULL AND \"Effort\" IS NOT NULL)");
            });

        migrationBuilder.CreateTable(
            name: "InstallationLimits",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                TaskDurationSeconds = table.Column<long>(type: "INTEGER", nullable: true),
                CpuMillis = table.Column<int>(type: "INTEGER", nullable: true),
                MemoryBytes = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InstallationLimits", x => x.Id);
                table.CheckConstraint("CK_InstallationLimits_Single", "\"Id\" = 1");
            });

        migrationBuilder.CreateTable(
            name: "ProtectedPaths",
            columns: table => new
            {
                Pattern = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProtectedPaths", x => x.Pattern);
            });

        migrationBuilder.CreateTable(
            name: "Settings",
            columns: table => new
            {
                Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Value = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Settings", x => x.Name);
            });

        migrationBuilder.InsertData(table: "ProtectedPaths", column: "Pattern", value: ".github/**");
        migrationBuilder.InsertData(table: "ProtectedPaths", column: "Pattern", value: "hephaestus.yml");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Aliases");

        migrationBuilder.DropTable(
            name: "InstallationLimits");

        migrationBuilder.DropTable(
            name: "ProtectedPaths");

        migrationBuilder.DropTable(
            name: "Settings");
    }
}
