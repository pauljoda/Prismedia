using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plugin_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    enabled_capabilities = table.Column<string>(type: "jsonb", nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: false),
                    protected_secrets = table.Column<string>(type: "jsonb", nullable: false),
                    effective_capabilities = table.Column<string>(type: "jsonb", nullable: false),
                    remote_instance_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    has_persistent_remote_identity = table.Column<bool>(type: "boolean", nullable: false),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_connections", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_integration_connections_plugin_id",
                table: "integration_connections",
                column: "plugin_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_connections");
        }
    }
}
