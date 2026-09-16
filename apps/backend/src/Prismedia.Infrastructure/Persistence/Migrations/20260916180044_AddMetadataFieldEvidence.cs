using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataFieldEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_metadata_fields",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(7,6)", precision: 7, scale: 6, nullable: true),
                    is_cleared = table.Column<bool>(type: "boolean", nullable: false),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_metadata_fields", x => new { x.entity_id, x.field });
                    table.ForeignKey(
                        name: "FK_entity_metadata_fields_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_metadata_fields");
        }
    }
}
