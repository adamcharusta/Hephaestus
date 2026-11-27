using Microsoft.EntityFrameworkCore.Migrations;

namespace Hephaestus.Persistence.Migrations;

/// <summary>
/// Lets a Trusted User's reply on an issue resume a waiting Task (Story 2.10, FR-49): a Signal for a created comment keeps the comment's text,
/// and the comments the loop admitted are kept for each Task with their hash, with the Escalation a comment answered. Nothing that exists is changed.
/// </summary>
internal partial class AddAdmittedComments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CommentBody",
            table: "Signals",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "AdmittedComments",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                TaskId = table.Column<string>(type: "TEXT", nullable: false),
                CommentId = table.Column<long>(type: "INTEGER", nullable: false),
                AuthorId = table.Column<long>(type: "INTEGER", nullable: false),
                AuthorLogin = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                Body = table.Column<string>(type: "TEXT", nullable: false),
                Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                SignalSequence = table.Column<long>(type: "INTEGER", nullable: false),
                AdmittedAt = table.Column<string>(type: "TEXT", nullable: false),
                AnswersEscalationId = table.Column<string>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AdmittedComments", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AdmittedComments_TaskId_CommentId",
            table: "AdmittedComments",
            columns: new[] { "TaskId", "CommentId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AdmittedComments");

        migrationBuilder.DropColumn(name: "CommentBody", table: "Signals");
    }
}
