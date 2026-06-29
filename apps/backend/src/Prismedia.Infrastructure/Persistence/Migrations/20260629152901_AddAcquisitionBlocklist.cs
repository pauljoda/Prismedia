using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAcquisitionBlocklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_redownload",
                table: "book_acquisition_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "acquisition_blocklist",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "failed"),
                    title = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    indexer_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    info_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    acquisition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_acquisition_blocklist", x => x.id);
                    table.ForeignKey(
                        name: "FK_acquisition_blocklist_acquisitions_acquisition_id",
                        column: x => x.acquisition_id,
                        principalTable: "acquisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_acquisition_blocklist_acquisition_id",
                table: "acquisition_blocklist",
                column: "acquisition_id");

            migrationBuilder.CreateIndex(
                name: "IX_acquisition_blocklist_identity",
                table: "acquisition_blocklist",
                column: "identity",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acquisition_blocklist");

            migrationBuilder.DropColumn(
                name: "auto_redownload",
                table: "book_acquisition_profiles");
        }
    }
}
