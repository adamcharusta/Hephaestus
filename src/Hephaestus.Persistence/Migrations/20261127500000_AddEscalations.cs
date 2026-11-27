using Microsoft.EntityFrameworkCore.Migrations;

namespace Hephaestus.Persistence.Migrations;

/// <summary>
/// Gives a Task somewhere to record the question Hephaestus asked the Owner (Story 2.9, FR-48): an Escalation, with the decision, the options,
/// the condition that ends the wait, and, for an effect that could not be completed, which effect and why. The database allows a Task one open
/// Escalation at a time, so that one Escalation asks for one decision. Nothing that exists is changed.
/// </summary>
internal partial class AddEscalations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Escalations",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                TaskId = table.Column<string>(type: "TEXT", nullable: false),
                Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Decision = table.Column<string>(type: "TEXT", nullable: false),
                Options = table.Column<string>(type: "TEXT", nullable: false),
                ResumeCondition = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Cause = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                RaisedAt = table.Column<string>(type: "TEXT", nullable: false),
                EffectKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                EffectKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                EffectDetail = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                EffectReason = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                ClosedAt = table.Column<string>(type: "TEXT", nullable: true),
                Outcome = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Escalations", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Escalations_Open",
            table: "Escalations",
            column: "TaskId",
            unique: true,
            filter: "\"ClosedAt\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Escalations_TaskId_RaisedAt",
            table: "Escalations",
            columns: new[] { "TaskId", "RaisedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Escalations");
    }
}
