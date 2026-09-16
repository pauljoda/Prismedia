using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "managed_controls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    holding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    active_holding_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "jsonb", nullable: false),
                    plan = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    problem = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_controls", x => x.id);
                    table.ForeignKey(
                        name: "FK_managed_controls_integration_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "integration_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_managed_controls_managed_holdings_holding_id",
                        column: x => x.holding_id,
                        principalTable: "managed_holdings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_managed_controls_active_holding_id",
                table: "managed_controls",
                column: "active_holding_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_managed_controls_connection_id",
                table: "managed_controls",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "IX_managed_controls_holding_id_created_at",
                table: "managed_controls",
                columns: new[] { "holding_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_managed_controls_next_check_at",
                table: "managed_controls",
                column: "next_check_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "managed_controls");
        }
    }
}
