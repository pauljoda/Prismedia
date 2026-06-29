using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monitors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "book"),
                    acquisition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "active"),
                    title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    author = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    last_searched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitors", x => x.id);
                    table.ForeignKey(
                        name: "FK_monitors_acquisitions_acquisition_id",
                        column: x => x.acquisition_id,
                        principalTable: "acquisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_monitors_acquisition_id",
                table: "monitors",
                column: "acquisition_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_monitors_status_last_searched_at",
                table: "monitors",
                columns: new[] { "status", "last_searched_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monitors");
        }
    }
}
