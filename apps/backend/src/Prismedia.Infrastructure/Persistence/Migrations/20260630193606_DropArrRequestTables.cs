using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropArrRequestTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_acquisitions_request_history_request_history_id",
                table: "acquisitions");

            migrationBuilder.DropTable(
                name: "request_history");

            migrationBuilder.DropTable(
                name: "request_service_credentials");

            migrationBuilder.DropTable(
                name: "request_service_instances");

            migrationBuilder.DropIndex(
                name: "IX_acquisitions_request_history_id",
                table: "acquisitions");

            migrationBuilder.DropColumn(
                name: "request_history_id",
                table: "acquisitions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "request_history_id",
                table: "acquisitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "request_service_instances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    default_metadata_profile_id = table.Column<int>(type: "integer", nullable: true),
                    default_quality_profile_id = table.Column<int>(type: "integer", nullable: true),
                    default_root_folder_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    default_tag_ids = table.Column<int[]>(type: "integer[]", nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    minimum_availability = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "released"),
                    search_on_request = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_service_instances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "request_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    external_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    monitored = table.Column<bool>(type: "boolean", nullable: false),
                    poster_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    selected_child_ids = table.Column<string[]>(type: "text[]", nullable: false),
                    service_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "submitted"),
                    status_message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    subtitle = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    upstream_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    year = table.Column<int>(type: "integer", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "request_service_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    credential_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    encrypted_value = table.Column<string>(type: "text", nullable: false),
                    service_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_service_credentials", x => x.id);
                    table.ForeignKey(
                        name: "FK_request_service_credentials_request_service_instances_servi~",
                        column: x => x.service_instance_id,
                        principalTable: "request_service_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_acquisitions_request_history_id",
                table: "acquisitions",
                column: "request_history_id");

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

            migrationBuilder.CreateIndex(
                name: "IX_request_service_credentials_service_instance_id_credential_~",
                table: "request_service_credentials",
                columns: new[] { "service_instance_id", "credential_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_request_service_instances_kind_is_default",
                table: "request_service_instances",
                columns: new[] { "kind", "is_default" });

            migrationBuilder.AddForeignKey(
                name: "FK_acquisitions_request_history_request_history_id",
                table: "acquisitions",
                column: "request_history_id",
                principalTable: "request_history",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
