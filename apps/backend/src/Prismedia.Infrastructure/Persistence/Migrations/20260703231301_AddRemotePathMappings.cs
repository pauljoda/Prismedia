using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRemotePathMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "remote_path_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    download_client_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    remote_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    local_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_remote_path_mappings", x => x.id);
                    table.ForeignKey(
                        name: "FK_remote_path_mappings_download_client_configs_download_clien~",
                        column: x => x.download_client_config_id,
                        principalTable: "download_client_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_remote_path_mappings_download_client_config_id",
                table: "remote_path_mappings",
                column: "download_client_config_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "remote_path_mappings");
        }
    }
}
