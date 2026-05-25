using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CollapseUniversalCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_favorite",
                table: "entities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_nsfw",
                table: "entities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_organized",
                table: "entities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "rating_value",
                table: "entities",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE entities SET
                    is_favorite = f.is_favorite,
                    is_nsfw = f.is_nsfw,
                    is_organized = f.is_organized
                FROM entity_flags f
                WHERE f.entity_id = entities.id;
                """);

            migrationBuilder.Sql("""
                UPDATE entities SET rating_value = r.value
                FROM entity_ratings r
                WHERE r.entity_id = entities.id;
                """);

            migrationBuilder.DropTable(
                name: "entity_flags");

            migrationBuilder.DropTable(
                name: "entity_ratings");

            migrationBuilder.AddCheckConstraint(
                name: "ck_entities_rating",
                table: "entities",
                sql: "rating_value IS NULL OR (rating_value >= 0 AND rating_value <= 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_entities_rating",
                table: "entities");

            migrationBuilder.CreateTable(
                name: "entity_flags",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_favorite = table.Column<bool>(type: "boolean", nullable: false),
                    is_nsfw = table.Column<bool>(type: "boolean", nullable: false),
                    is_organized = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_flags", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_entity_flags_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_ratings",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_ratings", x => x.entity_id);
                    table.CheckConstraint("ck_entity_ratings_value", "value >= 0 AND value <= 5");
                    table.ForeignKey(
                        name: "FK_entity_ratings_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO entity_flags (entity_id, is_favorite, is_nsfw, is_organized, updated_at)
                SELECT id, is_favorite, is_nsfw, is_organized, updated_at
                FROM entities
                WHERE is_favorite OR is_nsfw OR is_organized;
                """);

            migrationBuilder.Sql("""
                INSERT INTO entity_ratings (entity_id, value, updated_at)
                SELECT id, rating_value, updated_at
                FROM entities
                WHERE rating_value IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "is_favorite",
                table: "entities");

            migrationBuilder.DropColumn(
                name: "is_nsfw",
                table: "entities");

            migrationBuilder.DropColumn(
                name: "is_organized",
                table: "entities");

            migrationBuilder.DropColumn(
                name: "rating_value",
                table: "entities");
        }
    }
}
