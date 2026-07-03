using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexerStatusAndQueryLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "query_limit_per_hour",
                table: "indexer_configs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "indexer_statuses",
                columns: table => new
                {
                    indexer_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    escalation_level = table.Column<int>(type: "integer", nullable: false),
                    disabled_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_failure_message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_statuses", x => x.indexer_config_id);
                    table.ForeignKey(
                        name: "FK_indexer_statuses_indexer_configs_indexer_config_id",
                        column: x => x.indexer_config_id,
                        principalTable: "indexer_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "indexer_statuses");

            migrationBuilder.DropColumn(
                name: "query_limit_per_hour",
                table: "indexer_configs");
        }
    }
}
