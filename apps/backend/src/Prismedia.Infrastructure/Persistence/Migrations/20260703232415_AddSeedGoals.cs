using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeedGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "seed_ratio",
                table: "indexer_configs",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "seed_time_minutes",
                table: "indexer_configs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "seed_goal_ratio",
                table: "download_transfers",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "seed_goal_time_minutes",
                table: "download_transfers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "seeding_since",
                table: "download_transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "seed_ratio",
                table: "download_client_configs",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "seed_time_minutes",
                table: "download_client_configs",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "seed_ratio",
                table: "indexer_configs");

            migrationBuilder.DropColumn(
                name: "seed_time_minutes",
                table: "indexer_configs");

            migrationBuilder.DropColumn(
                name: "seed_goal_ratio",
                table: "download_transfers");

            migrationBuilder.DropColumn(
                name: "seed_goal_time_minutes",
                table: "download_transfers");

            migrationBuilder.DropColumn(
                name: "seeding_since",
                table: "download_transfers");

            migrationBuilder.DropColumn(
                name: "seed_ratio",
                table: "download_client_configs");

            migrationBuilder.DropColumn(
                name: "seed_time_minutes",
                table: "download_client_configs");
        }
    }
}
