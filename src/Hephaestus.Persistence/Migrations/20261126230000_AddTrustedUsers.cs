using Microsoft.EntityFrameworkCore.Migrations;


namespace Hephaestus.Persistence.Migrations;

/// <summary>Adds the Trusted Users (Story 1.8), identified by GitHub's numeric user id; the login is a display column.</summary>
internal partial class AddTrustedUsers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TrustedUsers",
            columns: table => new
            {
                UserId = table.Column<long>(type: "INTEGER", nullable: false),
                Login = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                AddedAt = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TrustedUsers", x => x.UserId);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TrustedUsers");
    }
}
