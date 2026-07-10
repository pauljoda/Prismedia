using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityLifecycleClaimsAndNormalizeMonitors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "lifecycle_claim_id",
                table: "entities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lifecycle_claim_kind",
                table: "entities",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lifecycle_claimed_at",
                table: "entities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_entities_lifecycle_claim",
                table: "entities",
                sql: "(lifecycle_claim_kind IS NULL AND lifecycle_claim_id IS NULL AND lifecycle_claimed_at IS NULL) OR (lifecycle_claim_kind IS NOT NULL AND lifecycle_claim_id IS NOT NULL AND lifecycle_claimed_at IS NOT NULL)");

            // Entity-linked monitors are durable intent, not one-shot acquisition rows. Older imports
            // marked them fulfilled before the generic Entity monitor model landed, which strands an
            // on-disk item after its files are removed. Preserve legacy acquisition-only semantics while
            // reactivating only monitors that now have the stable Entity target required for re-search.
            migrationBuilder.Sql(
                """
                UPDATE monitors
                SET monitor_preset = 'none',
                    updated_at = CURRENT_TIMESTAMP
                WHERE monitor_preset IN ('first-season', 'latest-season', 'pilot');

                UPDATE monitors
                SET status = 'active',
                    updated_at = CURRENT_TIMESTAMP
                WHERE entity_id IS NOT NULL
                  AND status = 'fulfilled';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Monitor normalization is intentionally irreversible: an Entity-linked Active row no longer
            // records whether it came from a legacy fulfilled acquisition or current durable intent.
            migrationBuilder.DropCheckConstraint(
                name: "ck_entities_lifecycle_claim",
                table: "entities");

            migrationBuilder.DropColumn(
                name: "lifecycle_claim_id",
                table: "entities");

            migrationBuilder.DropColumn(
                name: "lifecycle_claim_kind",
                table: "entities");

            migrationBuilder.DropColumn(
                name: "lifecycle_claimed_at",
                table: "entities");
        }
    }
}
