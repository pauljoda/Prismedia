using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProgressCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_entity_states_book_checkpoint_bounds",
                table: "user_entity_states");

            migrationBuilder.DropColumn(
                name: "listening_current_entity_id",
                table: "user_entity_states");

            migrationBuilder.DropColumn(
                name: "listening_index",
                table: "user_entity_states");

            migrationBuilder.DropColumn(
                name: "listening_marker_id",
                table: "user_entity_states");

            migrationBuilder.DropColumn(
                name: "listening_offset_seconds",
                table: "user_entity_states");

            migrationBuilder.DropColumn(
                name: "listening_total",
                table: "user_entity_states");

            migrationBuilder.DropColumn(
                name: "listening_track_entity_id",
                table: "user_entity_states");

            migrationBuilder.DropColumn(
                name: "listening_unit",
                table: "user_entity_states");

            migrationBuilder.DropColumn(
                name: "listening_updated_at",
                table: "user_entity_states");

            migrationBuilder.DropColumn(
                name: "reading_current_entity_id",
                table: "user_entity_states");

            migrationBuilder.DropColumn(
                name: "reading_index",
                table: "user_entity_states");

            migrationBuilder.DropColumn(
                name: "reading_location",
                table: "user_entity_states");

            migrationBuilder.DropColumn(
                name: "reading_mode",
                table: "user_entity_states");

            migrationBuilder.DropColumn(
                name: "reading_total",
                table: "user_entity_states");

            migrationBuilder.DropColumn(
                name: "reading_unit",
                table: "user_entity_states");

            migrationBuilder.DropColumn(
                name: "reading_updated_at",
                table: "user_entity_states");

            migrationBuilder.CreateTable(
                name: "user_progress_checkpoints",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modality = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    position_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    index = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<int>(type: "integer", nullable: false),
                    offset_seconds = table.Column<double>(type: "double precision", nullable: true),
                    marker_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    location = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_progress_checkpoints", x => new { x.user_id, x.entity_id, x.modality });
                    table.CheckConstraint("ck_user_progress_checkpoints_bounds", "\"index\" >= 0 AND total >= 0 AND \"index\" <= total AND (offset_seconds IS NULL OR offset_seconds >= 0)");
                    table.CheckConstraint("ck_user_progress_checkpoints_offset", "(modality IN ('listening')) = (offset_seconds IS NOT NULL) AND (marker_id IS NULL OR offset_seconds IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_user_progress_checkpoints_entities_position_entity_id",
                        column: x => x.position_entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_progress_checkpoints_entity_markers_marker_id",
                        column: x => x.marker_id,
                        principalTable: "entity_markers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_user_progress_checkpoints_user_entity_states_user_id_entity~",
                        columns: x => new { x.user_id, x.entity_id },
                        principalTable: "user_entity_states",
                        principalColumns: new[] { "user_id", "entity_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_progress_checkpoints_marker_id",
                table: "user_progress_checkpoints",
                column: "marker_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_progress_checkpoints_position_entity_id",
                table: "user_progress_checkpoints",
                column: "position_entity_id");

            BackfillReleasedBookPositions(migrationBuilder);
        }

        /// <summary>
        /// Seeds checkpoints from released row shapes only. A Book's main cursor in a readable unit
        /// becomes its reading checkpoint. Its listening checkpoint comes from an audio-only
        /// seconds cursor or, failing that, the pre-unification absolute resume time; either is
        /// placed on a physical track by walking the playable tracks' cumulative durations in
        /// (sort order, title, id) order. Books with an unprobed track keep no listening checkpoint.
        /// </summary>
        private static void BackfillReleasedBookPositions(MigrationBuilder migrationBuilder)
        {
            var book = EntityKind.Book.ToCode();
            var track = EntityKind.AudioTrack.ToCode();
            var source = EntityFileRole.Source.ToCode();
            var reading = ConsumptionModality.Reading.ToCode();
            var listening = ConsumptionModality.Listening.ToCode();
            var cfi = ProgressUnit.Cfi.ToCode();
            var page = ProgressUnit.Page.ToCode();
            var second = ProgressUnit.Second.ToCode();
            var readerModes = string.Join(", ", Enum.GetValues<ReaderMode>().Select(mode => $"'{mode.ToCode()}'"));
            var readableTotal = ConsumptionModalityDefinition.ReadablePositionTotal;

            migrationBuilder.Sql($"""
                INSERT INTO user_progress_checkpoints (
                    user_id, entity_id, modality, position_entity_id, unit, "index", total,
                    offset_seconds, marker_id, mode, location, updated_at)
                SELECT state.user_id, state.entity_id, '{reading}', state.progress_current_entity_id,
                       state.progress_unit, state.progress_index, state.progress_total, NULL, NULL,
                       CASE WHEN state.progress_mode IN ({readerModes}) THEN state.progress_mode END,
                       NULLIF(btrim(state.progress_location), ''),
                       COALESCE(state.progress_updated_at, state.updated_at)
                FROM user_entity_states AS state
                INNER JOIN entities AS book ON book.id = state.entity_id AND book.kind_code = '{book}'
                WHERE state.progress_current_entity_id IS NOT NULL
                  AND ((state.progress_unit = '{cfi}' AND state.progress_total = {readableTotal})
                    OR (state.progress_unit = '{page}' AND state.progress_total > 0))
                  AND state.progress_index BETWEEN 0 AND state.progress_total;
                """);

            migrationBuilder.Sql($"""
                WITH tracks AS (
                    SELECT audio.parent_entity_id AS book_id,
                           audio.id AS track_id,
                           technical.duration_seconds AS duration,
                           SUM(technical.duration_seconds) OVER track_order AS end_seconds,
                           ROW_NUMBER() OVER track_order AS position,
                           COUNT(*) OVER (PARTITION BY audio.parent_entity_id) AS track_count
                    FROM entities AS audio
                    INNER JOIN entities AS book ON book.id = audio.parent_entity_id AND book.kind_code = '{book}'
                    LEFT JOIN entity_technical AS technical ON technical.entity_id = audio.id
                    WHERE audio.kind_code = '{track}'
                      AND NOT audio.is_wanted
                      AND EXISTS (
                          SELECT 1 FROM entity_files AS file
                          WHERE file.entity_id = audio.id AND file.role = '{source}')
                    WINDOW track_order AS (
                        PARTITION BY audio.parent_entity_id
                        ORDER BY COALESCE(audio.sort_order, 2147483647), audio.title COLLATE "C", audio.id)
                ),
                probed_books AS (
                    SELECT book_id
                    FROM tracks
                    GROUP BY book_id
                    HAVING bool_and(duration IS NOT NULL AND duration > 0)
                ),
                positions AS (
                    SELECT state.user_id,
                           state.entity_id,
                           CASE WHEN state.progress_unit = '{second}' AND state.progress_current_entity_id IS NOT NULL
                                THEN state.progress_index::double precision
                                ELSE state.resume_seconds END AS absolute_seconds,
                           CASE WHEN state.progress_unit = '{second}' AND state.progress_current_entity_id IS NOT NULL
                                THEN COALESCE(state.progress_updated_at, state.updated_at)
                                ELSE COALESCE(state.last_active_at, state.updated_at) END AS updated_at
                    FROM user_entity_states AS state
                    INNER JOIN probed_books AS probed ON probed.book_id = state.entity_id
                    WHERE (state.progress_unit = '{second}' AND state.progress_current_entity_id IS NOT NULL)
                       OR state.resume_seconds > 0
                ),
                placed AS (
                    SELECT DISTINCT ON (position.user_id, position.entity_id)
                           position.user_id,
                           position.entity_id,
                           track.track_id,
                           track.duration,
                           LEAST(GREATEST(position.absolute_seconds - (track.end_seconds - track.duration), 0), track.duration)
                               AS offset_seconds,
                           position.updated_at
                    FROM positions AS position
                    INNER JOIN tracks AS track ON track.book_id = position.entity_id
                    WHERE position.absolute_seconds < track.end_seconds OR track.position = track.track_count
                    ORDER BY position.user_id, position.entity_id, track.position
                )
                INSERT INTO user_progress_checkpoints (
                    user_id, entity_id, modality, position_entity_id, unit, "index", total,
                    offset_seconds, marker_id, mode, location, updated_at)
                SELECT user_id, entity_id, '{listening}', track_id, '{second}',
                       FLOOR(offset_seconds)::integer,
                       GREATEST(FLOOR(offset_seconds)::integer, CEIL(duration)::integer),
                       offset_seconds, NULL, NULL, NULL, updated_at
                FROM placed;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_progress_checkpoints");

            migrationBuilder.AddColumn<Guid>(
                name: "listening_current_entity_id",
                table: "user_entity_states",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "listening_index",
                table: "user_entity_states",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "listening_marker_id",
                table: "user_entity_states",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "listening_offset_seconds",
                table: "user_entity_states",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "listening_total",
                table: "user_entity_states",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "listening_track_entity_id",
                table: "user_entity_states",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "listening_unit",
                table: "user_entity_states",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "listening_updated_at",
                table: "user_entity_states",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reading_current_entity_id",
                table: "user_entity_states",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reading_index",
                table: "user_entity_states",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reading_location",
                table: "user_entity_states",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reading_mode",
                table: "user_entity_states",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reading_total",
                table: "user_entity_states",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reading_unit",
                table: "user_entity_states",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reading_updated_at",
                table: "user_entity_states",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_entity_states_book_checkpoint_bounds",
                table: "user_entity_states",
                sql: "(reading_index IS NULL OR reading_index >= 0) AND (reading_total IS NULL OR reading_total >= 0) AND (listening_index IS NULL OR listening_index >= 0) AND (listening_total IS NULL OR listening_total >= 0) AND (listening_offset_seconds IS NULL OR listening_offset_seconds >= 0)");
        }
    }
}
