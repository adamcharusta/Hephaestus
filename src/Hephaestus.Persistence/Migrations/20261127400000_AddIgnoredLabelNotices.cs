using Microsoft.EntityFrameworkCore.Migrations;

namespace Hephaestus.Persistence.Migrations;

/// <summary>
/// Lets the effect journal hold a comment on an issue that is no Task (Story 2.5, AD-5): the Task of a line becomes optional, a line may name a
/// repository and an issue, and an intent may carry a notice key, of which the database allows one for each issue, so that a user who repeats an
/// action is answered once. The table is made again to make the Task optional, with its indexes and the triggers that make it append-only; the lines it holds
/// are copied as they are.
/// </summary>
internal partial class AddIgnoredLabelNotices : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""DROP TRIGGER "EffectJournal_NoUpdate";""");
        migrationBuilder.Sql("""DROP TRIGGER "EffectJournal_NoDelete";""");

        // SQLite cannot make a column optional in place, so the table is made again and its lines are copied as they are.
        migrationBuilder.Sql(
            """
            CREATE TABLE "EffectJournal_New" (
                "Sequence" INTEGER NOT NULL CONSTRAINT "PK_EffectJournal" PRIMARY KEY AUTOINCREMENT,
                "Key" TEXT NOT NULL,
                "Entry" TEXT NOT NULL,
                "TaskId" TEXT NULL,
                "RepositoryId" INTEGER NULL,
                "IssueNumber" INTEGER NULL,
                "Cause" TEXT NOT NULL,
                "Kind" TEXT NOT NULL,
                "Detail" TEXT NOT NULL,
                "NoticeKey" TEXT NULL,
                "Ordinal" INTEGER NOT NULL,
                "Result" TEXT NULL,
                "Reason" TEXT NULL,
                "Failures" INTEGER NOT NULL,
                "NextAttemptAt" TEXT NULL,
                "At" TEXT NOT NULL
            );
            """);
        migrationBuilder.Sql(
            """
            INSERT INTO "EffectJournal_New" ("Sequence", "Key", "Entry", "TaskId", "Cause", "Kind", "Detail", "Ordinal", "Result", "Reason", "Failures", "NextAttemptAt", "At")
            SELECT "Sequence", "Key", "Entry", "TaskId", "Cause", "Kind", "Detail", "Ordinal", "Result", "Reason", "Failures", "NextAttemptAt", "At" FROM "EffectJournal";
            """);
        migrationBuilder.Sql("""DROP TABLE "EffectJournal";""");
        migrationBuilder.Sql("""ALTER TABLE "EffectJournal_New" RENAME TO "EffectJournal";""");

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

        migrationBuilder.CreateIndex(
            name: "IX_EffectJournal_Notice",
            table: "EffectJournal",
            columns: new[] { "RepositoryId", "IssueNumber", "NoticeKey" },
            unique: true,
            filter: "\"Entry\" = 'intent' AND \"NoticeKey\" IS NOT NULL");

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
        throw new NotSupportedException("The effect journal is append-only, and what it holds for an issue that is no Task cannot be taken back.");
    }
}
