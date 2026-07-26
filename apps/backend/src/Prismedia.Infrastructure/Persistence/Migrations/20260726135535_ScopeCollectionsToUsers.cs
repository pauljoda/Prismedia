using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Prismedia.Domain.Entities;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeCollectionsToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_shared",
                table: "collection_details",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "owner_user_id",
                table: "collection_details",
                type: "uuid",
                nullable: true);

            var adminRoleCode = UserRole.Admin.ToCode();
            migrationBuilder.Sql($$"""
                UPDATE collection_details
                SET owner_user_id = COALESCE(
                    (SELECT id FROM users WHERE role = '{{adminRoleCode}}' ORDER BY created_at, id LIMIT 1),
                    (SELECT id FROM users ORDER BY created_at, id LIMIT 1))
                WHERE owner_user_id IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "owner_user_id",
                table: "collection_details",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_details_owner_user_id_is_shared",
                table: "collection_details",
                columns: new[] { "owner_user_id", "is_shared" });

            migrationBuilder.AddForeignKey(
                name: "FK_collection_details_users_owner_user_id",
                table: "collection_details",
                column: "owner_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_collection_details_users_owner_user_id",
                table: "collection_details");

            migrationBuilder.DropIndex(
                name: "IX_collection_details_owner_user_id_is_shared",
                table: "collection_details");

            migrationBuilder.DropColumn(
                name: "is_shared",
                table: "collection_details");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "collection_details");
        }
    }
}
