using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestServiceInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "request_service_instances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    default_root_folder_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    default_quality_profile_id = table.Column<int>(type: "integer", nullable: true),
                    default_metadata_profile_id = table.Column<int>(type: "integer", nullable: true),
                    search_on_request = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_service_instances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "request_service_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    encrypted_value = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                name: "IX_request_service_credentials_service_instance_id_credential_~",
                table: "request_service_credentials",
                columns: new[] { "service_instance_id", "credential_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_request_service_instances_kind_is_default",
                table: "request_service_instances",
                columns: new[] { "kind", "is_default" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "request_service_credentials");

            migrationBuilder.DropTable(
                name: "request_service_instances");
        }
    }
}
