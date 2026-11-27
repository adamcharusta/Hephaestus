using Microsoft.EntityFrameworkCore.Migrations;

namespace Hephaestus.Persistence.Migrations;

/// <summary>
/// Adds how each Agent signs in and the capacity record of each Agent credential (Story 3.10, FR-25, FR-26, AD-16, AD-20). The record keeps what a probe found as a message id and its parameters, never
/// text a CLI printed. Nothing that exists is changed.
/// </summary>
internal partial class AddAgentSetup : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AgentCapacities",
            columns: table => new
            {
                Agent = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Mode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Readiness = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                ReasonId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                ReasonArguments = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                ProbedAt = table.Column<string>(type: "TEXT", nullable: false),
                ExhaustedUntil = table.Column<string>(type: "TEXT", nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_AgentCapacities", x => new { x.Agent, x.Mode }));

        migrationBuilder.CreateTable(
            name: "AgentSettings",
            columns: table => new
            {
                Agent = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Mode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                ChosenAt = table.Column<string>(type: "TEXT", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_AgentSettings", x => x.Agent));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AgentCapacities");

        migrationBuilder.DropTable(name: "AgentSettings");
    }
}
