using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookModalityProgressCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
        }
    }
}
