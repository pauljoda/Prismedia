using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryFileChangeIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "library_file_change_intents",
                columns: table => new
                {
                    library_root_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scan_kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    path = table.Column<string>(type: "text", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_file_change_intents", x => new { x.library_root_id, x.scan_kind, x.path });
                    table.ForeignKey(
                        name: "FK_library_file_change_intents_library_roots_library_root_id",
                        column: x => x.library_root_id,
                        principalTable: "library_roots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_library_file_change_intents_observed_at",
                table: "library_file_change_intents",
                column: "observed_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "library_file_change_intents");
        }
    }
}
