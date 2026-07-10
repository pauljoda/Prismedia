using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillLegacyAcquisitionIdentityLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Repair every legacy acquisition whose provider identity resolves to exactly one Entity
            // of the same acquisition kind. Provider/value equality is deliberately exact: both sides
            // are canonicalized at their write boundaries. Ambiguous, missing, and kind-incompatible
            // identities fail closed and remain available for an explicit maintenance decision.
            migrationBuilder.Sql(
                """
                WITH candidates AS (
                    SELECT DISTINCT acquisition.id AS acquisition_id,
                           external_id.entity_id
                    FROM acquisitions AS acquisition
                    INNER JOIN entity_external_ids AS external_id
                      ON external_id.provider = acquisition.identity_namespace
                     AND external_id.value = acquisition.identity_value
                    INNER JOIN entities AS entity
                      ON entity.id = external_id.entity_id
                     AND entity.kind_code = acquisition.kind
                    WHERE acquisition.entity_id IS NULL
                      AND acquisition.identity_namespace IS NOT NULL
                      AND acquisition.identity_value IS NOT NULL
                ),
                unique_matches AS (
                    SELECT candidates.acquisition_id,
                           MAX(candidates.entity_id::text)::uuid AS entity_id
                    FROM candidates
                    GROUP BY candidates.acquisition_id
                    HAVING COUNT(*) = 1
                )
                UPDATE acquisitions AS acquisition
                SET entity_id = unique_matches.entity_id
                FROM unique_matches
                WHERE acquisition.id = unique_matches.acquisition_id
                  AND acquisition.entity_id IS NULL;
                """);

            // Once acquisitions have stable Entity links, promote one legacy monitor per Entity.
            // Existing canonical monitors always win. Primary acquisitions outrank upgrade-only
            // fallbacks; every duplicate row and transfer remains intact and target-resolvable.
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
            // Intentionally irreversible: a repaired link is indistinguishable from an EntityId
            // written by normal application behavior. Downgrade must preserve stable identity data.
        }
    }
}
