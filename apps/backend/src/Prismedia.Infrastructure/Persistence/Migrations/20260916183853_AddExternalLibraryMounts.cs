using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalLibraryMounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "external_library_mounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    library_root_id = table.Column<Guid>(type: "uuid", nullable: false),
                    remote_root_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    remote_path = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    local_path = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_library_mounts", x => x.id);
                    table.ForeignKey(
                        name: "FK_external_library_mounts_integration_connections_connection_~",
                        column: x => x.connection_id,
                        principalTable: "integration_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_external_library_mounts_library_roots_library_root_id",
                        column: x => x.library_root_id,
                        principalTable: "library_roots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_external_library_mounts_connection_id_remote_root_id",
                table: "external_library_mounts",
                columns: new[] { "connection_id", "remote_root_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_library_mounts_library_root_id",
                table: "external_library_mounts",
                column: "library_root_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_library_mounts");
        }
    }
}
