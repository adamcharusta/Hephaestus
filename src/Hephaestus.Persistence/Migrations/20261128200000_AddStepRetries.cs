using Microsoft.EntityFrameworkCore.Migrations;

namespace Hephaestus.Persistence.Migrations;

/// <summary>
/// Records what bounding the retries and the Task's time needs (Story 3.16, AD-11, NFR-5): which attempt a step run is, and the time a Task has spent in <c>running</c>. The time is kept by a trigger where the Task changes state, so that
/// no code that moves a Task has to remember it: leaving <c>running</c> adds the time since the Task entered it, entering it starts a new stretch, and a <c>failed</c> Task that is opted in again begins with none, because one that failed at its limit
/// could not otherwise go on. A Task that is running when the database is upgraded counts from its last update. Nothing that exists is changed otherwise.
/// </summary>
internal partial class AddStepRetries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Attempt",
            table: "StepRuns",
            type: "INTEGER",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<long>(
            name: "RunningMillis",
            table: "Tasks",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<string>(
            name: "RunningSince",
            table: "Tasks",
            type: "TEXT",
            nullable: true);

        migrationBuilder.Sql("UPDATE \"Tasks\" SET \"RunningSince\" = \"UpdatedAt\" WHERE \"State\" = 'running';");
        migrationBuilder.Sql(
            """
            CREATE TRIGGER "TR_Tasks_RunningTime" AFTER UPDATE OF "State" ON "Tasks"
            WHEN OLD."State" <> NEW."State"
            BEGIN
                UPDATE "Tasks" SET
                    "RunningMillis" = CASE
                        WHEN OLD."State" = 'failed' THEN 0
                        WHEN OLD."State" = 'running' AND OLD."RunningSince" IS NOT NULL
                            THEN OLD."RunningMillis" + MAX(0, CAST(ROUND((julianday(NEW."UpdatedAt") - julianday(OLD."RunningSince")) * 86400000.0) AS INTEGER))
                        ELSE OLD."RunningMillis"
                    END,
                    "RunningSince" = CASE WHEN NEW."State" = 'running' THEN NEW."UpdatedAt" ELSE NULL END
                WHERE "Id" = NEW."Id";
            END;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER \"TR_Tasks_RunningTime\";");

        migrationBuilder.DropColumn(
            name: "Attempt",
            table: "StepRuns");

        migrationBuilder.DropColumn(
            name: "RunningMillis",
            table: "Tasks");

        migrationBuilder.DropColumn(
            name: "RunningSince",
            table: "Tasks");
    }
}
