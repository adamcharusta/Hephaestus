using Microsoft.EntityFrameworkCore.Migrations;

namespace Hephaestus.Persistence.Migrations;

/// <summary>
/// Records on a step-exited Signal the hosts the egress proxy refused during the step run (Story 3.4, AD-7, AD-18). Nothing that exists is changed: a
/// Signal that was appended earlier has no refused host.
/// </summary>
internal partial class AddRefusedHosts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "RefusedHosts", table: "Signals", type: "TEXT", nullable: true);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "RefusedHosts", table: "Signals");
}
