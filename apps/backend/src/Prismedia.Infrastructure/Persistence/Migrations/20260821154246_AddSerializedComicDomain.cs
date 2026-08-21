using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSerializedComicDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "comic_installment_details",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installment_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comic_installment_details", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_comic_installment_details_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "comic_series_details",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comic_series_details", x => x.entity_id);
                    table.ForeignKey(
                        name: "FK_comic_series_details_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "entity_kinds",
                columns: new[] { "code", "category", "display_name", "storage_shape" },
                values: new object[,]
                {
                    { "comic-installment", "Media", "Comic Installment", "archive" },
                    { "comic-series", "Media", "Comic Series", "folder" },
                    { "comic-volume", "Media", "Comic Volume", "none" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comic_installment_details");

            migrationBuilder.DropTable(
                name: "comic_series_details");

            migrationBuilder.DeleteData(
                table: "entity_kinds",
                keyColumn: "code",
                keyValue: "comic-installment");

            migrationBuilder.DeleteData(
                table: "entity_kinds",
                keyColumn: "code",
                keyValue: "comic-series");

            migrationBuilder.DeleteData(
                table: "entity_kinds",
                keyColumn: "code",
                keyValue: "comic-volume");
        }
    }
}
