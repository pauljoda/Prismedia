using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillLegacyJobGraphs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Recovery pass for databases that applied an early graph migration build before the legacy
            // lane-aware backfill was added to RemoveLegacyJobPriorityLane. Fresh upgrades are already
            // complete; this pass is idempotent and repairs only still-orphaned rows.
            migrationBuilder.Sql("""
                INSERT INTO job_graphs (
                    id,
                    origin,
                    status,
                    display_name,
                    root_run_id,
                    initiating_user_id,
                    root_entity_kind,
                    root_entity_id,
                    active_key,
                    cancellation_requested,
                    last_dispatched_at,
                    created_at,
                    updated_at,
                    finished_at)
                SELECT
                    run.id,
                    CASE
                        WHEN run.type IN ('identify-search', 'bulk-identify', 'identify-cascade')
                            THEN 'interactive'
                        ELSE 'background'
                    END,
                    run.status,
                    COALESCE(NULLIF(run.target_label, ''), run.type),
                    run.id,
                    NULL,
                    run.target_entity_kind,
                    run.target_entity_id,
                    NULL,
                    run.status = 'cancelled',
                    run.started_at,
                    run.created_at,
                    COALESCE(run.finished_at, run.started_at, run.created_at),
                    run.finished_at
                FROM job_runs AS run
                WHERE run.graph_id IS NULL
                ON CONFLICT (id) DO NOTHING;

                UPDATE job_runs
                SET graph_id = id,
                    node_key = COALESCE(node_key, 'legacy:' || id::text),
                    sequence = 0
                WHERE graph_id IS NULL;

                UPDATE identify_queue_items AS item
                SET job_graph_id = COALESCE(search.graph_id, cascade.graph_id)
                FROM identify_queue_items AS source
                LEFT JOIN job_runs AS search ON search.id = source.search_job_id
                LEFT JOIN job_runs AS cascade ON cascade.id = source.cascade_job_id
                WHERE item.id = source.id
                  AND item.job_graph_id IS NULL
                  AND COALESCE(search.graph_id, cascade.graph_id) IS NOT NULL;

                UPDATE acquisitions AS acquisition
                SET job_graph_id = (
                    SELECT run.graph_id
                    FROM job_runs AS run
                    WHERE run.target_entity_id = acquisition.id::text
                      AND run.graph_id IS NOT NULL
                    ORDER BY
                        CASE run.status WHEN 'running' THEN 0 WHEN 'queued' THEN 1 ELSE 2 END,
                        run.created_at DESC
                    LIMIT 1
                )
                WHERE acquisition.job_graph_id IS NULL
                  AND EXISTS (
                    SELECT 1
                    FROM job_runs AS run
                    WHERE run.target_entity_id = acquisition.id::text
                      AND run.graph_id IS NOT NULL
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
