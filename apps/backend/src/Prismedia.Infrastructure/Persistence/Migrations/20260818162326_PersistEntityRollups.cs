using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Prismedia.Infrastructure.Persistence;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistEntityRollups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_collection_member_counts",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    library_root_id = table.Column<Guid>(type: "uuid", nullable: false),
                    count_total = table.Column<int>(type: "integer", nullable: false),
                    count_nsfw = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_collection_member_counts", x => new { x.entity_id, x.library_root_id });
                    table.ForeignKey(
                        name: "FK_entity_collection_member_counts_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_descendant_counts",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    descendant_kind_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    library_root_id = table.Column<Guid>(type: "uuid", nullable: false),
                    count_total = table.Column<int>(type: "integer", nullable: false),
                    count_nsfw = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_descendant_counts", x => new { x.entity_id, x.descendant_kind_code, x.library_root_id });
                    table.ForeignKey(
                        name: "FK_entity_descendant_counts_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_reference_counts",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_kind_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    library_root_id = table.Column<Guid>(type: "uuid", nullable: false),
                    count_total = table.Column<int>(type: "integer", nullable: false),
                    count_nsfw = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_reference_counts", x => new { x.entity_id, x.source_kind_code, x.library_root_id });
                    table.ForeignKey(
                        name: "FK_entity_reference_counts_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_rollups",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_library_root_id = table.Column<Guid>(type: "uuid", nullable: true),
                    effective_is_nsfw = table.Column<bool>(type: "boolean", nullable: false),
                    direct_child_count = table.Column<int>(type: "integer", nullable: false),
                    latest_descendant_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_rollups", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_entity_rollups_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(EntityRollupProjectionSql.Create);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EntityRollupProjectionSql.Drop);

            migrationBuilder.DropTable(
                name: "entity_collection_member_counts");

            migrationBuilder.DropTable(
                name: "entity_descendant_counts");

            migrationBuilder.DropTable(
                name: "entity_reference_counts");

            migrationBuilder.DropTable(
                name: "entity_rollups");
        }
    }
}
