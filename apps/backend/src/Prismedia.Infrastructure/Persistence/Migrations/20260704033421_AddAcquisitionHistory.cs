using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAcquisitionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "acquisition_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    acquisition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "book"),
                    @event = table.Column<string>(name: "event", type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "grabbed"),
                    title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    release_title = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    indexer_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    download_client_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    quality_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    format_score = table.Column<int>(type: "integer", nullable: true),
                    message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_acquisition_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_acquisition_history_acquisitions_acquisition_id",
                        column: x => x.acquisition_id,
                        principalTable: "acquisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_acquisition_history_acquisition_id",
                table: "acquisition_history",
                column: "acquisition_id");

            migrationBuilder.CreateIndex(
                name: "IX_acquisition_history_created_at",
                table: "acquisition_history",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_acquisition_history_entity_id",
                table: "acquisition_history",
                column: "entity_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acquisition_history");
        }
    }
}
