using Microsoft.EntityFrameworkCore.Migrations;


namespace Hephaestus.Persistence.Migrations;

/// <summary>
/// Adds the persisted Signal inbox and the poller's cursors (Story 2.1, AD-1). A Signal's sequence is the inbox order and its
/// source with its source event id is unique, so a second append of an event is refused; a cursor is one row per Project and stream.
/// A Project that is already registered is watched from the moment of the upgrade.
/// </summary>
internal partial class AddSignalInbox : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PollCursors",
            columns: table => new
            {
                RepositoryId = table.Column<long>(type: "INTEGER", nullable: false),
                Stream = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Position = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                ETag = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PollCursors", x => new { x.RepositoryId, x.Stream });
            });

        migrationBuilder.CreateTable(
            name: "Signals",
            columns: table => new
            {
                Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                SourceEventId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                RepositoryId = table.Column<long>(type: "INTEGER", nullable: false),
                IssueNumber = table.Column<int>(type: "INTEGER", nullable: false),
                ActorId = table.Column<long>(type: "INTEGER", nullable: true),
                ActorLogin = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                CommentId = table.Column<long>(type: "INTEGER", nullable: true),
                CommentAuthorId = table.Column<long>(type: "INTEGER", nullable: true),
                OccurredAt = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Signals", x => x.Sequence);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Signals_Source_SourceEventId",
            table: "Signals",
            columns: new[] { "Source", "SourceEventId" },
            unique: true);

        // A Project registered before polling existed has been watched by nobody: it is watched from this upgrade on, as one registered now.
        migrationBuilder.Sql(
            """
            INSERT INTO "PollCursors" ("RepositoryId", "Stream", "Position", "ETag")
            SELECT "RepositoryId", 'since', strftime('%Y-%m-%dT%H:%M:%S', 'now') || '.0000000Z', NULL FROM "Projects" WHERE "RemovedAt" IS NULL
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PollCursors");

        migrationBuilder.DropTable(
            name: "Signals");
    }
}
