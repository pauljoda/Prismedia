using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAcquisitionTeardownClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_monitors_book_entity_id",
                table: "monitors");

            // Preserve every legacy monitor and its acquisition/download ownership. A duplicate cannot also
            // take the unique canonical EntityId slot, so first make its acquisition link an equivalent
            // generic Entity anchor. Existing canonical rows win; otherwise exactly one legacy row is
            // promoted and acquisition-linked duplicates remain fully operable through AcquisitionId.
            migrationBuilder.Sql(
                """
                UPDATE acquisitions AS acquisition
                SET entity_id = monitor.book_entity_id
                FROM monitors AS monitor
                WHERE monitor.book_entity_id IS NOT NULL
                  AND monitor.acquisition_id = acquisition.id
                  AND acquisition.entity_id IS NULL;

                UPDATE acquisitions AS upgrade
                SET entity_id = monitor.book_entity_id
                FROM monitors AS monitor
                WHERE monitor.book_entity_id IS NOT NULL
                  AND monitor.upgrade_child_acquisition_id = upgrade.id
                  AND upgrade.entity_id IS NULL;

                WITH promotable AS (
                    SELECT monitor.id,
                           ROW_NUMBER() OVER (
                               PARTITION BY monitor.book_entity_id
                               -- Prefer a container/no-acquisition monitor because acquisition-linked
                               -- duplicates retain an equivalent Entity route through their acquisition.
                               ORDER BY CASE WHEN monitor.acquisition_id IS NULL THEN 0 ELSE 1 END,
                                        monitor.created_at,
                                        monitor.id) AS row_number
                    FROM monitors AS monitor
                    WHERE monitor.entity_id IS NULL
                      AND monitor.book_entity_id IS NOT NULL
                      AND NOT EXISTS (
                          SELECT 1
                          FROM monitors AS canonical
                          WHERE canonical.entity_id = monitor.book_entity_id)
                )
                UPDATE monitors AS monitor
                SET entity_id = monitor.book_entity_id
                FROM promotable
                WHERE monitor.id = promotable.id
                  AND promotable.row_number = 1;

                -- Generic monitors created before stable EntityId attachment may never have carried the
                -- book-only alias. Promote their primary acquisition target (or upgrade child as fallback)
                -- under the same canonical-wins rule. Every duplicate row remains acquisition-resolvable.
                WITH resolved AS (
                    SELECT monitor.id,
                           COALESCE(acquisition.entity_id, upgrade.entity_id) AS target_entity_id,
                           monitor.acquisition_id,
                           monitor.created_at
                    FROM monitors AS monitor
                    LEFT JOIN acquisitions AS acquisition
                      ON acquisition.id = monitor.acquisition_id
                    LEFT JOIN acquisitions AS upgrade
                      ON upgrade.id = monitor.upgrade_child_acquisition_id
                    WHERE monitor.entity_id IS NULL
                      AND COALESCE(acquisition.entity_id, upgrade.entity_id) IS NOT NULL
                ),
                promotable AS (
                    SELECT resolved.id,
                           resolved.target_entity_id,
                           ROW_NUMBER() OVER (
                               PARTITION BY resolved.target_entity_id
                               ORDER BY CASE WHEN resolved.acquisition_id IS NOT NULL THEN 0 ELSE 1 END,
                                        resolved.created_at,
                                        resolved.id) AS row_number
                    FROM resolved
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM monitors AS canonical
                        WHERE canonical.entity_id = resolved.target_entity_id)
                )
                UPDATE monitors AS monitor
                SET entity_id = promotable.target_entity_id
                FROM promotable
                WHERE monitor.id = promotable.id
                  AND promotable.row_number = 1;
                """);

            migrationBuilder.DropColumn(
                name: "book_entity_id",
                table: "monitors");

            migrationBuilder.AddColumn<string>(
                name: "teardown_intent",
                table: "acquisitions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "teardown_original_status",
                table: "acquisitions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "teardown_replacement_acquisition_id",
                table: "acquisitions",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "teardown_intent",
                table: "acquisitions");

            migrationBuilder.DropColumn(
                name: "teardown_original_status",
                table: "acquisitions");

            migrationBuilder.DropColumn(
                name: "teardown_replacement_acquisition_id",
                table: "acquisitions");

            migrationBuilder.AddColumn<Guid>(
                name: "book_entity_id",
                table: "monitors",
                type: "uuid",
                nullable: true);

            // Rehydrate the legacy alias from the strongest remaining generic link. This restores every
            // pre-upgrade acquisition-backed duplicate exactly; post-upgrade Entity monitors also downgrade
            // with their canonical identity represented in the legacy slot.
            migrationBuilder.Sql(
                """
                UPDATE monitors AS monitor
                SET book_entity_id = COALESCE(
                    monitor.entity_id,
                    (
                        SELECT acquisition.entity_id
                        FROM acquisitions AS acquisition
                        WHERE acquisition.id = monitor.acquisition_id),
                    (
                        SELECT upgrade.entity_id
                        FROM acquisitions AS upgrade
                        WHERE upgrade.id = monitor.upgrade_child_acquisition_id));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_monitors_book_entity_id",
                table: "monitors",
                column: "book_entity_id");
        }
    }
}
