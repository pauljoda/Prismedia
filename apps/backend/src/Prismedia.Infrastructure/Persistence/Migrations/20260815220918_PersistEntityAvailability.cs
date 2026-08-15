using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Prismedia.Infrastructure.Persistence;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistEntityAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_availability",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    has_source_media = table.Column<bool>(type: "boolean", nullable: false),
                    latest_acquisition_status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    acquisition_status_codes = table.Column<string[]>(type: "text[]", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_availability", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_entity_availability_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_entities_kind_code_created_at_id",
                table: "entities",
                columns: new[] { "kind_code", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_entity_availability_has_source_media",
                table: "entity_availability",
                column: "has_source_media");

            migrationBuilder.Sql(EntityAvailabilityProjectionSql.Create);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EntityAvailabilityProjectionSql.Drop);

            migrationBuilder.DropTable(
                name: "entity_availability");

            migrationBuilder.DropIndex(
                name: "IX_entities_kind_code_created_at_id",
                table: "entities");
        }
    }
}
