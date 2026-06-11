using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "request_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    external_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    subtitle = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    year = table.Column<int>(type: "integer", nullable: true),
                    poster_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    upstream_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    monitored = table.Column<bool>(type: "boolean", nullable: false),
                    selected_child_ids = table.Column<string[]>(type: "text[]", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "submitted"),
                    status_message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_request_history_request_service_instances_service_instance_~",
                        column: x => x.service_instance_id,
                        principalTable: "request_service_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_request_history_created_at",
                table: "request_history",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_request_history_service_instance_id",
                table: "request_history",
                column: "service_instance_id");

            migrationBuilder.CreateIndex(
                name: "IX_request_history_source_kind_external_id",
                table: "request_history",
                columns: new[] { "source", "kind", "external_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "request_history");
        }
    }
}
