using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAcquisitionEntityLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "entity_id",
                table: "acquisitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "entity_id",
                table: "acquisition_import_hints",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_acquisitions_entity_id",
                table: "acquisitions",
                column: "entity_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_acquisitions_entity_id",
                table: "acquisitions");

            migrationBuilder.DropColumn(
                name: "entity_id",
                table: "acquisitions");

            migrationBuilder.DropColumn(
                name: "entity_id",
                table: "acquisition_import_hints");
        }
    }
}
