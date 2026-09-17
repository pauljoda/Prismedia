using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedOwnershipRelease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_managed_holdings_connection_id_kind_remote_id",
                table: "managed_holdings");

            migrationBuilder.AddColumn<Guid>(
                name: "release_operation_id",
                table: "managed_holdings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "release_request",
                table: "managed_holdings",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "released_at",
                table: "managed_holdings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "released_bindings",
                table: "managed_holdings",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateIndex(
                name: "IX_managed_holdings_connection_id_kind_remote_id",
                table: "managed_holdings",
                columns: new[] { "connection_id", "kind", "remote_id" },
                unique: true,
                filter: "released_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_managed_holdings_release_operation_id",
                table: "managed_holdings",
                column: "release_operation_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_managed_holdings_connection_id_kind_remote_id",
                table: "managed_holdings");

            migrationBuilder.DropIndex(
                name: "IX_managed_holdings_release_operation_id",
                table: "managed_holdings");

            migrationBuilder.DropColumn(
                name: "release_operation_id",
                table: "managed_holdings");

            migrationBuilder.DropColumn(
                name: "release_request",
                table: "managed_holdings");

            migrationBuilder.DropColumn(
                name: "released_at",
                table: "managed_holdings");

            migrationBuilder.DropColumn(
                name: "released_bindings",
                table: "managed_holdings");

            migrationBuilder.CreateIndex(
                name: "IX_managed_holdings_connection_id_kind_remote_id",
                table: "managed_holdings",
                columns: new[] { "connection_id", "kind", "remote_id" },
                unique: true);
        }
    }
}
