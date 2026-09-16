using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFulfillmentReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fulfillment_reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    book_rendition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    external_ids = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fulfillment_reservations", x => x.id);
                    table.ForeignKey(
                        name: "FK_fulfillment_reservations_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fulfillment_reservations_integration_connections_connection~",
                        column: x => x.connection_id,
                        principalTable: "integration_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fulfillment_reservations_connection_id",
                table: "fulfillment_reservations",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "IX_fulfillment_reservations_entity_id",
                table: "fulfillment_reservations",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_fulfillment_reservations_owner_id_owner_kind_entity_id_book~",
                table: "fulfillment_reservations",
                columns: new[] { "owner_id", "owner_kind", "entity_id", "book_rendition" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
            migrationBuilder.Sql(BackfillOwnership);
            migrationBuilder.Sql(OwnershipGuards);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RemoveOwnershipGuards);
            migrationBuilder.DropTable(
                name: "fulfillment_reservations");
        }
    }
}
