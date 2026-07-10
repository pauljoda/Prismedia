using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PromoteGenericMonitorEntityIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Correct databases that already applied AddAcquisitionTeardownClaims before generic monitor
            // promotion was added to that migration. Existing canonical EntityId rows always win. When
            // several legacy monitors resolve to one Entity, one deterministic primary-acquisition row is
            // promoted and every duplicate remains intact and target-resolvable through its acquisition.
            migrationBuilder.Sql(
                """
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally irreversible: after promotion there is no durable way to distinguish a
            // corrected legacy link from an EntityId written by normal application behavior. Clearing
            // either would silently destroy stable monitoring intent, so downgrade preserves the data.
        }
    }
}
