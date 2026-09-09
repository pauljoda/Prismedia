using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHeldAcquisitionAlternatives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "recovery_of_acquisition_id",
                table: "acquisitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_acquisitions_recovery_of_acquisition_id",
                table: "acquisitions",
                column: "recovery_of_acquisition_id");

            migrationBuilder.AddForeignKey(
                name: "FK_acquisitions_acquisitions_recovery_of_acquisition_id",
                table: "acquisitions",
                column: "recovery_of_acquisition_id",
                principalTable: "acquisitions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_acquisitions_acquisitions_recovery_of_acquisition_id",
                table: "acquisitions");

            migrationBuilder.DropIndex(
                name: "IX_acquisitions_recovery_of_acquisition_id",
                table: "acquisitions");

            migrationBuilder.DropColumn(
                name: "recovery_of_acquisition_id",
                table: "acquisitions");
        }
    }
}
