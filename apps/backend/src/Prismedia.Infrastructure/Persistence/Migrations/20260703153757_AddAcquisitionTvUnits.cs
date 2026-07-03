using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAcquisitionTvUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "episode_number",
                table: "acquisitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "season_number",
                table: "acquisitions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "episode_number",
                table: "acquisitions");

            migrationBuilder.DropColumn(
                name: "season_number",
                table: "acquisitions");
        }
    }
}
