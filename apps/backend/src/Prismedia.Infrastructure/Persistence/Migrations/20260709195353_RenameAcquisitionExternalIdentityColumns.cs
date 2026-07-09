using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameAcquisitionExternalIdentityColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "plugin_item_id",
                table: "acquisitions",
                newName: "identity_value");

            migrationBuilder.RenameColumn(
                name: "plugin_id",
                table: "acquisitions",
                newName: "identity_namespace");

            migrationBuilder.RenameColumn(
                name: "plugin_item_id",
                table: "acquisition_import_hints",
                newName: "identity_value");

            migrationBuilder.RenameColumn(
                name: "plugin_id",
                table: "acquisition_import_hints",
                newName: "identity_namespace");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "identity_value",
                table: "acquisitions",
                newName: "plugin_item_id");

            migrationBuilder.RenameColumn(
                name: "identity_namespace",
                table: "acquisitions",
                newName: "plugin_id");

            migrationBuilder.RenameColumn(
                name: "identity_value",
                table: "acquisition_import_hints",
                newName: "plugin_item_id");

            migrationBuilder.RenameColumn(
                name: "identity_namespace",
                table: "acquisition_import_hints",
                newName: "plugin_id");
        }
    }
}
