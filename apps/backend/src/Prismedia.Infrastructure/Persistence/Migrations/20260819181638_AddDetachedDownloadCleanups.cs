using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDetachedDownloadCleanups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "detached_download_cleanups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_acquisition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    download_client_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_item_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_detached_download_cleanups", x => x.id);
                    table.ForeignKey(
                        name: "FK_detached_download_cleanups_acquisitions_source_acquisition_~",
                        column: x => x.source_acquisition_id,
                        principalTable: "acquisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_detached_download_cleanups_download_client_configs_download~",
                        column: x => x.download_client_config_id,
                        principalTable: "download_client_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_detached_download_cleanups_download_client_config_id",
                table: "detached_download_cleanups",
                column: "download_client_config_id");

            migrationBuilder.CreateIndex(
                name: "IX_detached_download_cleanups_source_acquisition_id",
                table: "detached_download_cleanups",
                column: "source_acquisition_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "detached_download_cleanups");
        }
    }
}
