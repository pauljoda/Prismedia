using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitorEntityLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "entity_id",
                table: "monitors",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_monitors_entity_id",
                table: "monitors",
                column: "entity_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_monitors_entity_id",
                table: "monitors");

            migrationBuilder.DropColumn(
                name: "entity_id",
                table: "monitors");
        }
    }
}
