using Microsoft.EntityFrameworkCore.Migrations;

namespace Hephaestus.Persistence.Migrations;

/// <summary>
/// Keeps with the intent of an effect the recorded data it is composed from (Story 3.13, AD-5): the facts of a pull request, so that a restart composes the same description. An intent that has none has a null payload, and nothing that exists
/// is changed.
/// </summary>
internal partial class AddPullRequestPayload : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "Payload", table: "EffectJournal", type: "TEXT", nullable: true);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "Payload", table: "EffectJournal");
}
