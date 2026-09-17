using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "managed_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    library_root_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    phase = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    state = table.Column<string>(type: "jsonb", nullable: false),
                    plan = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    problem = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_requests", x => x.id);
                    table.ForeignKey(
                        name: "FK_managed_requests_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_managed_requests_integration_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "integration_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_managed_requests_library_roots_library_root_id",
                        column: x => x.library_root_id,
                        principalTable: "library_roots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_managed_requests_connection_id_created_at",
                table: "managed_requests",
                columns: new[] { "connection_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_managed_requests_entity_id",
                table: "managed_requests",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_managed_requests_library_root_id",
                table: "managed_requests",
                column: "library_root_id");

            migrationBuilder.CreateIndex(
                name: "IX_managed_requests_next_check_at",
                table: "managed_requests",
                column: "next_check_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "managed_requests");
        }
    }
}
