using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedPeopleEnrichmentState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "people_enrichment_completed_at",
                table: "managed_holdings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "people_enrichment_fingerprint",
                table: "managed_holdings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "people_enrichment_completed_at",
                table: "managed_holdings");

            migrationBuilder.DropColumn(
                name: "people_enrichment_fingerprint",
                table: "managed_holdings");
        }
    }
}
