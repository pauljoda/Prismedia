using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientPriorityAndProfileDownloadCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "priority",
                table: "download_client_configs",
                type: "integer",
                nullable: false,
                defaultValue: 25);

            migrationBuilder.AddColumn<string>(
                name: "download_category",
                table: "book_acquisition_profiles",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "priority",
                table: "download_client_configs");

            migrationBuilder.DropColumn(
                name: "download_category",
                table: "book_acquisition_profiles");
        }
    }
}
