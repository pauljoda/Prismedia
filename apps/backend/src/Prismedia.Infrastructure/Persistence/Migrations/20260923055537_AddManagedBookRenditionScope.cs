using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedBookRenditionScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_managed_holdings_connection_id_kind_remote_id",
                table: "managed_holdings");

            migrationBuilder.AddColumn<string>(
                name: "book_rendition",
                table: "managed_holdings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_managed_holdings_connection_id_kind_remote_id_book_rendition",
                table: "managed_holdings",
                columns: new[] { "connection_id", "kind", "remote_id", "book_rendition" },
                unique: true,
                filter: "released_at IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_managed_holdings_connection_id_kind_remote_id_book_rendition",
                table: "managed_holdings");

            migrationBuilder.DropColumn(
                name: "book_rendition",
                table: "managed_holdings");

            migrationBuilder.CreateIndex(
                name: "IX_managed_holdings_connection_id_kind_remote_id",
                table: "managed_holdings",
                columns: new[] { "connection_id", "kind", "remote_id" },
                unique: true,
                filter: "released_at IS NULL");
        }
    }
}
