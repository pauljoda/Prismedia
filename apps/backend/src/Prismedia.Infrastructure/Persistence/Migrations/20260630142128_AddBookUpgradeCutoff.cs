using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookUpgradeCutoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "barren_searches",
                table: "monitors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "book_entity_id",
                table: "monitors",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "upgrade_attempts",
                table: "monitors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "upgrade_child_acquisition_id",
                table: "monitors",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_tier",
                table: "book_details",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "cutoff_format_tier",
                table: "book_acquisition_profiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "cutoff_source_tier",
                table: "book_acquisition_profiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<bool>(
                name: "upgrade_until_cutoff",
                table: "book_acquisition_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "owned_format_tier",
                table: "acquisitions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "owned_source_tier",
                table: "acquisitions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<Guid>(
                name: "upgrade_of_acquisition_id",
                table: "acquisitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "upgrade_quality_captured",
                table: "acquisitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "owned_format_tier",
                table: "acquisition_import_hints",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "owned_source_tier",
                table: "acquisition_import_hints",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.CreateIndex(
                name: "IX_monitors_book_entity_id",
                table: "monitors",
                column: "book_entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_acquisitions_upgrade_of_acquisition_id",
                table: "acquisitions",
                column: "upgrade_of_acquisition_id");

            migrationBuilder.AddForeignKey(
                name: "FK_acquisitions_acquisitions_upgrade_of_acquisition_id",
                table: "acquisitions",
                column: "upgrade_of_acquisition_id",
                principalTable: "acquisitions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_acquisitions_acquisitions_upgrade_of_acquisition_id",
                table: "acquisitions");

            migrationBuilder.DropIndex(
                name: "IX_monitors_book_entity_id",
                table: "monitors");

            migrationBuilder.DropIndex(
                name: "IX_acquisitions_upgrade_of_acquisition_id",
                table: "acquisitions");

            migrationBuilder.DropColumn(
                name: "barren_searches",
                table: "monitors");

            migrationBuilder.DropColumn(
                name: "book_entity_id",
                table: "monitors");

            migrationBuilder.DropColumn(
                name: "upgrade_attempts",
                table: "monitors");

            migrationBuilder.DropColumn(
                name: "upgrade_child_acquisition_id",
                table: "monitors");

            migrationBuilder.DropColumn(
                name: "source_tier",
                table: "book_details");

            migrationBuilder.DropColumn(
                name: "cutoff_format_tier",
                table: "book_acquisition_profiles");

            migrationBuilder.DropColumn(
                name: "cutoff_source_tier",
                table: "book_acquisition_profiles");

            migrationBuilder.DropColumn(
                name: "upgrade_until_cutoff",
                table: "book_acquisition_profiles");

            migrationBuilder.DropColumn(
                name: "owned_format_tier",
                table: "acquisitions");

            migrationBuilder.DropColumn(
                name: "owned_source_tier",
                table: "acquisitions");

            migrationBuilder.DropColumn(
                name: "upgrade_of_acquisition_id",
                table: "acquisitions");

            migrationBuilder.DropColumn(
                name: "upgrade_quality_captured",
                table: "acquisitions");

            migrationBuilder.DropColumn(
                name: "owned_format_tier",
                table: "acquisition_import_hints");

            migrationBuilder.DropColumn(
                name: "owned_source_tier",
                table: "acquisition_import_hints");
        }
    }
}
