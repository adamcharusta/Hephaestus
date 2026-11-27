using Microsoft.EntityFrameworkCore.Migrations;

namespace Hephaestus.Persistence.Migrations;

/// <summary>
/// Records on an Escalation the check that blocked a push (Story 3.7, AD-9, AD-19), and makes room in the journal for the intent of a push, whose detail names the step run, the bundle's checksum, and three commits. SQLite
/// keeps no length for text, so the longer detail changes the model and not a column.
/// </summary>
internal partial class AddPushBlockedEscalations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "Check", table: "Escalations", type: "TEXT", maxLength: 16, nullable: true);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "Check", table: "Escalations");
}
