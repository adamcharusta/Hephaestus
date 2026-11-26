using Microsoft.EntityFrameworkCore.Migrations;


namespace Hephaestus.Persistence.Migrations;

/// <summary>Adds the id of Hephaestus's own GitHub App (Story 1.7); the private key is a secret file, not a column.</summary>
internal partial class AddGitHubAppConnection : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "GitHubAppConnections",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                AppId = table.Column<long>(type: "INTEGER", nullable: false),
                ConnectedAt = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GitHubAppConnections", x => x.Id);
                table.CheckConstraint("CK_GitHubAppConnections_Single", "\"Id\" = 1");
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "GitHubAppConnections");
    }
}
