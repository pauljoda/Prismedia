using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestServiceDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int[]>(
                name: "default_tag_ids",
                table: "request_service_instances",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<string>(
                name: "minimum_availability",
                table: "request_service_instances",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "released");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_tag_ids",
                table: "request_service_instances");

            migrationBuilder.DropColumn(
                name: "minimum_availability",
                table: "request_service_instances");
        }
    }
}
