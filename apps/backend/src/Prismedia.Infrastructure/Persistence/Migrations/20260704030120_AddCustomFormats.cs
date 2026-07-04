using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomFormats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cutoff_format_score",
                table: "book_acquisition_profiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "format_scores_json",
                table: "book_acquisition_profiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<int>(
                name: "min_format_score",
                table: "book_acquisition_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "owned_format_score",
                table: "acquisitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "custom_formats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "book"),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    conditions_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_formats", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_custom_formats_kind",
                table: "custom_formats",
                column: "kind");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_formats");

            migrationBuilder.DropColumn(
                name: "cutoff_format_score",
                table: "book_acquisition_profiles");

            migrationBuilder.DropColumn(
                name: "format_scores_json",
                table: "book_acquisition_profiles");

            migrationBuilder.DropColumn(
                name: "min_format_score",
                table: "book_acquisition_profiles");

            migrationBuilder.DropColumn(
                name: "owned_format_score",
                table: "acquisitions");
        }
    }
}
