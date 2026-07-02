using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWantedSuppressions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wanted_suppressions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    item_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "book"),
                    title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wanted_suppressions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wanted_suppressions_provider_item_id",
                table: "wanted_suppressions",
                columns: new[] { "provider", "item_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wanted_suppressions");
        }
    }
}
