using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPluginInvocationAdmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plugin_invocation_states",
                columns: table => new
                {
                    plugin_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    next_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_invocation_states", x => x.plugin_id);
                });

            migrationBuilder.CreateTable(
                name: "plugin_invocation_leases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plugin_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_invocation_leases", x => x.id);
                    table.ForeignKey(
                        name: "FK_plugin_invocation_leases_plugin_invocation_states_plugin_id",
                        column: x => x.plugin_id,
                        principalTable: "plugin_invocation_states",
                        principalColumn: "plugin_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_plugin_invocation_leases_plugin_id_expires_at",
                table: "plugin_invocation_leases",
                columns: new[] { "plugin_id", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plugin_invocation_leases");

            migrationBuilder.DropTable(
                name: "plugin_invocation_states");
        }
    }
}
