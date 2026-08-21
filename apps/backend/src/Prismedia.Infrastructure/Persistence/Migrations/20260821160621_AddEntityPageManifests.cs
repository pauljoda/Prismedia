using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityPageManifests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_page_manifests",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    default_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    cover_ordinal = table.Column<int>(type: "integer", nullable: true),
                    source_signature = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_page_manifests", x => x.entity_id);
                    table.CheckConstraint("ck_entity_page_manifests_cover_ordinal", "cover_ordinal IS NULL OR cover_ordinal >= 0");
                    table.ForeignKey(
                        name: "FK_entity_page_manifests_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_page_entries",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    archive_member = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    page_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_double_page = table.Column<bool>(type: "boolean", nullable: false),
                    checksum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_page_entries", x => new { x.entity_id, x.ordinal });
                    table.CheckConstraint("ck_entity_page_entries_dimensions", "(width IS NULL OR width >= 0) AND (height IS NULL OR height >= 0)");
                    table.CheckConstraint("ck_entity_page_entries_ordinal", "ordinal >= 0");
                    table.ForeignKey(
                        name: "FK_entity_page_entries_entity_page_manifests_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity_page_manifests",
                        principalColumn: "entity_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_entity_page_entries_entity_id_archive_member",
                table: "entity_page_entries",
                columns: new[] { "entity_id", "archive_member" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_page_entries");

            migrationBuilder.DropTable(
                name: "entity_page_manifests");
        }
    }
}
