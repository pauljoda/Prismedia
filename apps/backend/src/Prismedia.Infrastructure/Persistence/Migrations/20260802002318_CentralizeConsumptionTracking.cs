using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class CentralizeConsumptionTracking : Migration {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropIndex(
            name: "IX_user_entity_states_user_id_last_played_at",
            table: "user_entity_states");

        migrationBuilder.RenameColumn(
            name: "play_duration_seconds",
            table: "user_entity_states",
            newName: "active_seconds");
        migrationBuilder.RenameColumn(
            name: "last_played_at",
            table: "user_entity_states",
            newName: "last_active_at");

        migrationBuilder.AddColumn<int>(
            name: "access_count",
            table: "user_entity_states",
            type: "integer",
            nullable: false,
            defaultValue: 0);
        migrationBuilder.AddColumn<int>(
            name: "completion_count",
            table: "user_entity_states",
            type: "integer",
            nullable: false,
            defaultValue: 0);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "last_accessed_at",
            table: "user_entity_states",
            type: "timestamp with time zone",
            nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "progress_consumed_count",
            table: "user_entity_states",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.Sql(
            """
            UPDATE user_entity_states
            SET completion_count = play_count,
                access_count = play_count + skip_count,
                last_accessed_at = last_active_at,
                progress_consumed_count = CASE
                    WHEN progress_total <= 0 OR progress_current_entity_id IS NULL THEN 0
                    WHEN progress_completed_at IS NOT NULL THEN progress_total
                    ELSE LEAST(GREATEST(progress_index + 1, 0), progress_total)
                END;
            """);
        migrationBuilder.DropColumn(name: "play_count", table: "user_entity_states");

        migrationBuilder.DropIndex(name: "IX_entity_playback_events_occurred_at", table: "entity_playback_events");
        migrationBuilder.DropIndex(name: "IX_entity_playback_events_entity_id_occurred_at", table: "entity_playback_events");
        migrationBuilder.DropIndex(name: "IX_entity_playback_events_kind_occurred_at", table: "entity_playback_events");
        migrationBuilder.DropIndex(name: "IX_entity_playback_events_user_id_occurred_at", table: "entity_playback_events");
        migrationBuilder.RenameTable(name: "entity_playback_events", newName: "entity_consumption_events");
        migrationBuilder.AddColumn<string>(
            name: "session_id",
            table: "entity_consumption_events",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "entity_consumption_days",
            columns: table => new {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: true),
                kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                activity_date = table.Column<DateOnly>(type: "date", nullable: false),
                duration_seconds = table.Column<double>(type: "double precision", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_entity_consumption_days", x => x.id);
                table.CheckConstraint("ck_entity_consumption_days_duration", "duration_seconds > 0");
                table.ForeignKey(
                    name: "FK_entity_consumption_days_entities_entity_id",
                    column: x => x.entity_id,
                    principalTable: "entities",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_entity_consumption_days_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.Sql(
            """
            INSERT INTO entity_consumption_days
                (id, entity_id, user_id, kind, activity_date, duration_seconds, updated_at)
            SELECT md5(entity_id::text || COALESCE(user_id::text, '') || kind ||
                       ((occurred_at AT TIME ZONE 'UTC')::date)::text)::uuid,
                   entity_id,
                   user_id,
                   kind,
                   (occurred_at AT TIME ZONE 'UTC')::date,
                   SUM(duration_seconds),
                   MAX(created_at)
            FROM entity_activity_events
            GROUP BY entity_id, user_id, kind, (occurred_at AT TIME ZONE 'UTC')::date;
            """);
        migrationBuilder.DropTable(name: "entity_activity_events");

        migrationBuilder.CreateIndex(
            name: "IX_user_entity_states_user_id_last_active_at",
            table: "user_entity_states",
            columns: new[] { "user_id", "last_active_at" },
            filter: "last_active_at IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_entity_consumption_days_activity_date", table: "entity_consumption_days", column: "activity_date");
        migrationBuilder.CreateIndex(
            name: "IX_entity_consumption_days_entity_id_activity_date",
            table: "entity_consumption_days",
            columns: new[] { "entity_id", "activity_date" });
        migrationBuilder.CreateIndex(
            name: "IX_entity_consumption_days_user_id_activity_date",
            table: "entity_consumption_days",
            columns: new[] { "user_id", "activity_date" });
        migrationBuilder.CreateIndex(
            name: "IX_entity_consumption_days_user_id_entity_id_kind_activity_date",
            table: "entity_consumption_days",
            columns: new[] { "user_id", "entity_id", "kind", "activity_date" },
            unique: true);
        migrationBuilder.CreateIndex(name: "IX_entity_consumption_events_occurred_at", table: "entity_consumption_events", column: "occurred_at");
        migrationBuilder.CreateIndex(
            name: "IX_entity_consumption_events_entity_id_occurred_at",
            table: "entity_consumption_events",
            columns: new[] { "entity_id", "occurred_at" });
        migrationBuilder.CreateIndex(
            name: "IX_entity_consumption_events_kind_occurred_at",
            table: "entity_consumption_events",
            columns: new[] { "kind", "occurred_at" });
        migrationBuilder.CreateIndex(
            name: "IX_entity_consumption_events_user_id_occurred_at",
            table: "entity_consumption_events",
            columns: new[] { "user_id", "occurred_at" });
        migrationBuilder.CreateIndex(
            name: "IX_entity_consumption_events_user_id_session_id_kind",
            table: "entity_consumption_events",
            columns: new[] { "user_id", "session_id", "kind" },
            unique: true,
            filter: "session_id IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropIndex(name: "IX_user_entity_states_user_id_last_active_at", table: "user_entity_states");

        migrationBuilder.DropIndex(name: "IX_entity_consumption_events_occurred_at", table: "entity_consumption_events");
        migrationBuilder.DropIndex(name: "IX_entity_consumption_events_entity_id_occurred_at", table: "entity_consumption_events");
        migrationBuilder.DropIndex(name: "IX_entity_consumption_events_kind_occurred_at", table: "entity_consumption_events");
        migrationBuilder.DropIndex(name: "IX_entity_consumption_events_user_id_occurred_at", table: "entity_consumption_events");
        migrationBuilder.DropIndex(name: "IX_entity_consumption_events_user_id_session_id_kind", table: "entity_consumption_events");
        migrationBuilder.DropColumn(name: "session_id", table: "entity_consumption_events");
        migrationBuilder.RenameTable(name: "entity_consumption_events", newName: "entity_playback_events");

        migrationBuilder.AddColumn<int>(
            name: "play_count",
            table: "user_entity_states",
            type: "integer",
            nullable: false,
            defaultValue: 0);
        migrationBuilder.Sql("UPDATE user_entity_states SET play_count = completion_count;");
        migrationBuilder.DropColumn(name: "access_count", table: "user_entity_states");
        migrationBuilder.DropColumn(name: "completion_count", table: "user_entity_states");
        migrationBuilder.DropColumn(name: "last_accessed_at", table: "user_entity_states");
        migrationBuilder.DropColumn(name: "progress_consumed_count", table: "user_entity_states");
        migrationBuilder.RenameColumn(name: "last_active_at", table: "user_entity_states", newName: "last_played_at");
        migrationBuilder.RenameColumn(name: "active_seconds", table: "user_entity_states", newName: "play_duration_seconds");

        migrationBuilder.CreateTable(
            name: "entity_activity_events",
            columns: table => new {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                duration_seconds = table.Column<double>(type: "double precision", nullable: false),
                entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table => {
                table.PrimaryKey("PK_entity_activity_events", x => x.id);
                table.CheckConstraint("ck_entity_activity_events_duration", "duration_seconds > 0");
                table.ForeignKey(
                    name: "FK_entity_activity_events_entities_entity_id",
                    column: x => x.entity_id,
                    principalTable: "entities",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_entity_activity_events_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.Sql(
            """
            INSERT INTO entity_activity_events
                (id, entity_id, user_id, kind, occurred_at, duration_seconds, created_at)
            SELECT id,
                   entity_id,
                   user_id,
                   kind,
                   activity_date::timestamp AT TIME ZONE 'UTC',
                   duration_seconds,
                   updated_at
            FROM entity_consumption_days;
            """);
        migrationBuilder.DropTable(name: "entity_consumption_days");

        migrationBuilder.CreateIndex(
            name: "IX_user_entity_states_user_id_last_played_at",
            table: "user_entity_states",
            columns: new[] { "user_id", "last_played_at" },
            filter: "last_played_at IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_entity_activity_events_occurred_at", table: "entity_activity_events", column: "occurred_at");
        migrationBuilder.CreateIndex(
            name: "IX_entity_activity_events_entity_id_occurred_at",
            table: "entity_activity_events",
            columns: new[] { "entity_id", "occurred_at" });
        migrationBuilder.CreateIndex(
            name: "IX_entity_activity_events_user_id_occurred_at",
            table: "entity_activity_events",
            columns: new[] { "user_id", "occurred_at" });
        migrationBuilder.CreateIndex(name: "IX_entity_playback_events_occurred_at", table: "entity_playback_events", column: "occurred_at");
        migrationBuilder.CreateIndex(
            name: "IX_entity_playback_events_entity_id_occurred_at",
            table: "entity_playback_events",
            columns: new[] { "entity_id", "occurred_at" });
        migrationBuilder.CreateIndex(
            name: "IX_entity_playback_events_kind_occurred_at",
            table: "entity_playback_events",
            columns: new[] { "kind", "occurred_at" });
        migrationBuilder.CreateIndex(
            name: "IX_entity_playback_events_user_id_occurred_at",
            table: "entity_playback_events",
            columns: new[] { "user_id", "occurred_at" });
    }
}
