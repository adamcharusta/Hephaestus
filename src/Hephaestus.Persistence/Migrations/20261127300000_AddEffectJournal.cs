using Microsoft.EntityFrameworkCore.Migrations;


namespace Hephaestus.Persistence.Migrations;

/// <summary>
/// Adds the effect journal (Story 2.4, AD-5): one line for the intent of each effect on GitHub, committed with the state change that causes
/// it, and one for each retry and for the result, recorded after the change was made. It is append-only, and the database enforces it: a
/// line is never changed or removed. An effect has one intent and at most one result.
/// </summary>
internal partial class AddEffectJournal : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EffectJournal",
            columns: table => new
            {
                Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Entry = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                TaskId = table.Column<string>(type: "TEXT", nullable: false),
                Cause = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Detail = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                Result = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                Reason = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                Failures = table.Column<int>(type: "INTEGER", nullable: false),
                NextAttemptAt = table.Column<string>(type: "TEXT", nullable: true),
                At = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EffectJournal", x => x.Sequence);
            });

        migrationBuilder.CreateIndex(
            name: "IX_EffectJournal_Key_Intent",
            table: "EffectJournal",
            column: "Key",
            unique: true,
            filter: "\"Entry\" = 'intent'");

        migrationBuilder.CreateIndex(
            name: "IX_EffectJournal_Key_Result",
            table: "EffectJournal",
            column: "Key",
            unique: true,
            filter: "\"Entry\" = 'result'");

        migrationBuilder.CreateIndex(
            name: "IX_EffectJournal_Key_Sequence",
            table: "EffectJournal",
            columns: new[] { "Key", "Sequence" });

        migrationBuilder.CreateIndex(
            name: "IX_EffectJournal_TaskId_Sequence",
            table: "EffectJournal",
            columns: new[] { "TaskId", "Sequence" });

        // A Task that exists already, made by Story 2.2's loop, has had no label shown. Each gets the intent of the effect that shows its state,
        // caused by the Signal that made it, as the loop writes it for a new Task; the first tick of the loop makes it.
        migrationBuilder.Sql(
            """
            INSERT INTO "EffectJournal" ("Key", "Entry", "TaskId", "Cause", "Kind", "Detail", "Ordinal", "Failures", "At")
            SELECT t."Id" || ':g' || s."SignalSequence" || ':label:0', 'intent', t."Id", 'g' || s."SignalSequence", 'label', t."State", 0, 0,
                   strftime('%Y-%m-%dT%H:%M:%S', 'now') || '.0000000Z'
            FROM "Tasks" t
            JOIN "IssueSnapshots" s ON s."TaskId" = t."Id" AND s."Id" = (SELECT MIN(first."Id") FROM "IssueSnapshots" first WHERE first."TaskId" = t."Id");
            """);

        // Append-only is the database's rule, not only the code's: a line of the journal cannot be changed or removed by anything.
        migrationBuilder.Sql(
            """
            CREATE TRIGGER "EffectJournal_NoUpdate" BEFORE UPDATE ON "EffectJournal"
            BEGIN SELECT RAISE(ABORT, 'The effect journal is append-only.'); END;
            """);
        migrationBuilder.Sql(
            """
            CREATE TRIGGER "EffectJournal_NoDelete" BEFORE DELETE ON "EffectJournal"
            BEGIN SELECT RAISE(ABORT, 'The effect journal is append-only.'); END;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "EffectJournal");
    }
}
