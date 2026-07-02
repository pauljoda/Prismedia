using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestTargeting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "profile_id",
                table: "monitors",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_library_root_id",
                table: "monitors",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "book_acquisition_profiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "book");

            migrationBuilder.AddColumn<Guid>(
                name: "target_library_root_id",
                table: "acquisitions",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "profile_id",
                table: "monitors");

            migrationBuilder.DropColumn(
                name: "target_library_root_id",
                table: "monitors");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "book_acquisition_profiles");

            migrationBuilder.DropColumn(
                name: "target_library_root_id",
                table: "acquisitions");
        }
    }
}
