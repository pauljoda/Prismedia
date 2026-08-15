using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL-owned write projection for Entity availability. Statement-level triggers collapse
/// batch mutations into one ancestor refresh, while the full reconcile command is the worker's
/// idempotent self-heal path.
/// </summary>
internal static class EntityAvailabilityProjectionSql {
    internal const string ReconcileCommand = "SELECT prismedia_reconcile_entity_availability();";

    internal static string Create {
        get {
            var sourceRole = EntityFileRole.Source.ToCode();
            var activeUpgradeStatuses = string.Join(", ", new[] {
                AcquisitionStatus.Pending,
                AcquisitionStatus.Searching,
                AcquisitionStatus.AwaitingSelection,
                AcquisitionStatus.Queued,
                AcquisitionStatus.Downloading,
                AcquisitionStatus.WaitingForDownloadClient,
                AcquisitionStatus.Downloaded,
                AcquisitionStatus.Importing,
                AcquisitionStatus.Stopping,
            }.Select(status => $"'{status.ToCode()}'"));

            return $$"""
                CREATE INDEX "IX_entity_availability_acquisition_status_codes"
                ON entity_availability USING GIN (acquisition_status_codes);

                CREATE OR REPLACE FUNCTION prismedia_refresh_entity_availability_roots(requested_root_ids uuid[])
                RETURNS integer
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    refreshed_count integer;
                BEGIN
                    WITH RECURSIVE requested_roots(root_id, entity_id, path) AS (
                        SELECT entity.id, entity.id, ARRAY[entity.id]
                        FROM entities AS entity
                        WHERE cardinality(requested_root_ids) = 0
                           OR entity.id = ANY (requested_root_ids)
                    ),
                    entity_tree(root_id, entity_id, path) AS (
                        SELECT root_id, entity_id, path
                        FROM requested_roots
                        UNION ALL
                        SELECT tree.root_id, child.id, tree.path || child.id
                        FROM entity_tree AS tree
                        INNER JOIN entities AS child ON child.parent_entity_id = tree.entity_id
                        WHERE NOT child.id = ANY (tree.path)
                    ),
                    source_state AS (
                        SELECT tree.root_id
                        FROM entity_tree AS tree
                        INNER JOIN entity_files AS file ON file.entity_id = tree.entity_id
                        WHERE file.role = '{{sourceRole}}'
                        GROUP BY tree.root_id
                    ),
                    direct_ranked AS (
                        SELECT
                            tree.root_id,
                            tree.entity_id,
                            acquisition.id AS acquisition_id,
                            acquisition.status,
                            acquisition.created_at,
                            ROW_NUMBER() OVER (
                                PARTITION BY tree.root_id, tree.entity_id
                                ORDER BY acquisition.created_at DESC, acquisition.id DESC
                            ) AS direct_rank
                        FROM entity_tree AS tree
                        INNER JOIN acquisitions AS acquisition ON acquisition.entity_id = tree.entity_id
                    ),
                    direct_latest AS (
                        SELECT root_id, entity_id, acquisition_id, status, created_at
                        FROM direct_ranked
                        WHERE direct_rank = 1
                    ),
                    upgrade_tree(root_id, anchor_acquisition_id, acquisition_id, status, created_at, path) AS (
                        SELECT
                            direct.root_id,
                            direct.acquisition_id,
                            child.id,
                            child.status,
                            child.created_at,
                            ARRAY[direct.acquisition_id, child.id]
                        FROM direct_latest AS direct
                        INNER JOIN acquisitions AS child
                            ON child.upgrade_of_acquisition_id = direct.acquisition_id
                        UNION ALL
                        SELECT
                            tree.root_id,
                            tree.anchor_acquisition_id,
                            child.id,
                            child.status,
                            child.created_at,
                            tree.path || child.id
                        FROM upgrade_tree AS tree
                        INNER JOIN acquisitions AS child
                            ON child.upgrade_of_acquisition_id = tree.acquisition_id
                        WHERE NOT child.id = ANY (tree.path)
                    ),
                    upgrade_ranked AS (
                        SELECT
                            root_id,
                            anchor_acquisition_id,
                            acquisition_id,
                            status,
                            ROW_NUMBER() OVER (
                                PARTITION BY root_id, anchor_acquisition_id
                                ORDER BY created_at DESC, acquisition_id DESC
                            ) AS latest_rank
                        FROM upgrade_tree
                    ),
                    selected_statuses AS (
                        SELECT root_id, status
                        FROM direct_latest
                        UNION ALL
                        SELECT root_id, status
                        FROM upgrade_ranked
                        WHERE status = ANY (ARRAY[{{activeUpgradeStatuses}}]::text[])
                           OR latest_rank = 1
                    ),
                    status_state AS (
                        SELECT root_id, ARRAY_AGG(DISTINCT status::text ORDER BY status::text) AS statuses
                        FROM selected_statuses
                        GROUP BY root_id
                    ),
                    direct_state AS (
                        SELECT root_id, status
                        FROM direct_latest
                        WHERE entity_id = root_id
                    ),
                    refreshed AS (
                        SELECT
                            root.id,
                            source.root_id IS NOT NULL AS has_source_media,
                            direct.status AS latest_acquisition_status,
                            COALESCE(statuses.statuses, ARRAY[]::text[]) AS acquisition_status_codes
                        FROM entities AS root
                        LEFT JOIN source_state AS source ON source.root_id = root.id
                        LEFT JOIN direct_state AS direct ON direct.root_id = root.id
                        LEFT JOIN status_state AS statuses ON statuses.root_id = root.id
                        WHERE cardinality(requested_root_ids) = 0
                           OR root.id = ANY (requested_root_ids)
                    )
                    INSERT INTO entity_availability (
                        entity_id,
                        has_source_media,
                        latest_acquisition_status,
                        acquisition_status_codes,
                        updated_at)
                    SELECT
                        refreshed.id,
                        refreshed.has_source_media,
                        refreshed.latest_acquisition_status,
                        refreshed.acquisition_status_codes,
                        CURRENT_TIMESTAMP
                    FROM refreshed
                    ON CONFLICT (entity_id) DO UPDATE
                    SET has_source_media = EXCLUDED.has_source_media,
                        latest_acquisition_status = EXCLUDED.latest_acquisition_status,
                        acquisition_status_codes = EXCLUDED.acquisition_status_codes,
                        updated_at = EXCLUDED.updated_at
                    WHERE entity_availability.has_source_media IS DISTINCT FROM EXCLUDED.has_source_media
                       OR entity_availability.latest_acquisition_status IS DISTINCT FROM EXCLUDED.latest_acquisition_status
                       OR entity_availability.acquisition_status_codes IS DISTINCT FROM EXCLUDED.acquisition_status_codes;

                    GET DIAGNOSTICS refreshed_count = ROW_COUNT;
                    RETURN refreshed_count;
                END;
                $function$;

                CREATE OR REPLACE FUNCTION prismedia_refresh_entity_availability_targets(target_entity_ids uuid[])
                RETURNS integer
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    root_ids uuid[];
                BEGIN
                    IF target_entity_ids IS NULL OR cardinality(target_entity_ids) = 0 THEN
                        RETURN 0;
                    END IF;

                    WITH RECURSIVE ancestors(entity_id, parent_entity_id, path) AS (
                        SELECT entity.id, entity.parent_entity_id, ARRAY[entity.id]
                        FROM entities AS entity
                        WHERE entity.id = ANY (target_entity_ids)
                        UNION ALL
                        SELECT parent.id, parent.parent_entity_id, ancestors.path || parent.id
                        FROM ancestors
                        INNER JOIN entities AS parent ON parent.id = ancestors.parent_entity_id
                        WHERE NOT parent.id = ANY (ancestors.path)
                    )
                    SELECT COALESCE(ARRAY_AGG(DISTINCT entity_id), ARRAY[]::uuid[])
                    INTO root_ids
                    FROM ancestors;

                    RETURN prismedia_refresh_entity_availability_roots(root_ids);
                END;
                $function$;

                CREATE OR REPLACE FUNCTION prismedia_reconcile_entity_availability()
                RETURNS integer
                LANGUAGE sql
                AS $function$
                    SELECT prismedia_refresh_entity_availability_roots(ARRAY[]::uuid[]);
                $function$;

                CREATE OR REPLACE FUNCTION prismedia_acquisition_target_entity(acquisition_id uuid)
                RETURNS uuid
                LANGUAGE sql
                STABLE
                AS $function$
                    WITH RECURSIVE chain(id, entity_id, upgrade_of_acquisition_id, depth, path) AS (
                        SELECT acquisition.id, acquisition.entity_id, acquisition.upgrade_of_acquisition_id, 0, ARRAY[acquisition.id]
                        FROM acquisitions AS acquisition
                        WHERE acquisition.id = acquisition_id
                        UNION ALL
                        SELECT parent.id, parent.entity_id, parent.upgrade_of_acquisition_id, chain.depth + 1, chain.path || parent.id
                        FROM chain
                        INNER JOIN acquisitions AS parent ON parent.id = chain.upgrade_of_acquisition_id
                        WHERE NOT parent.id = ANY (chain.path)
                    )
                    SELECT entity_id
                    FROM chain
                    WHERE entity_id IS NOT NULL
                    ORDER BY depth
                    LIMIT 1;
                $function$;

                CREATE OR REPLACE FUNCTION prismedia_refresh_acquisition_insert()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    target_ids uuid[];
                BEGIN
                    SELECT ARRAY_AGG(DISTINCT target_id)
                    INTO target_ids
                    FROM (
                        SELECT COALESCE(row.entity_id, prismedia_acquisition_target_entity(row.id)) AS target_id
                        FROM new_rows AS row
                    ) AS targets
                    WHERE target_id IS NOT NULL;
                    PERFORM prismedia_refresh_entity_availability_targets(target_ids);
                    RETURN NULL;
                END;
                $function$;

                CREATE OR REPLACE FUNCTION prismedia_refresh_acquisition_update()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    target_ids uuid[];
                BEGIN
                    SELECT ARRAY_AGG(DISTINCT target_id)
                    INTO target_ids
                    FROM (
                        SELECT COALESCE(row.entity_id, prismedia_acquisition_target_entity(row.id)) AS target_id
                        FROM new_rows AS row
                        UNION ALL
                        SELECT COALESCE(row.entity_id, prismedia_acquisition_target_entity(row.upgrade_of_acquisition_id)) AS target_id
                        FROM old_rows AS row
                    ) AS targets
                    WHERE target_id IS NOT NULL;
                    PERFORM prismedia_refresh_entity_availability_targets(target_ids);
                    RETURN NULL;
                END;
                $function$;

                CREATE OR REPLACE FUNCTION prismedia_refresh_acquisition_delete()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    target_ids uuid[];
                BEGIN
                    SELECT ARRAY_AGG(DISTINCT target_id)
                    INTO target_ids
                    FROM (
                        SELECT COALESCE(row.entity_id, prismedia_acquisition_target_entity(row.upgrade_of_acquisition_id)) AS target_id
                        FROM old_rows AS row
                    ) AS targets
                    WHERE target_id IS NOT NULL;
                    PERFORM prismedia_refresh_entity_availability_targets(target_ids);
                    RETURN NULL;
                END;
                $function$;

                CREATE OR REPLACE FUNCTION prismedia_refresh_source_file_insert()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    target_ids uuid[];
                BEGIN
                    SELECT ARRAY_AGG(DISTINCT entity_id)
                    INTO target_ids
                    FROM new_rows
                    WHERE role = '{{sourceRole}}';
                    PERFORM prismedia_refresh_entity_availability_targets(target_ids);
                    RETURN NULL;
                END;
                $function$;

                CREATE OR REPLACE FUNCTION prismedia_refresh_source_file_update()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    target_ids uuid[];
                BEGIN
                    SELECT ARRAY_AGG(DISTINCT entity_id)
                    INTO target_ids
                    FROM (
                        SELECT entity_id FROM new_rows WHERE role = '{{sourceRole}}'
                        UNION ALL
                        SELECT entity_id FROM old_rows WHERE role = '{{sourceRole}}'
                    ) AS targets;
                    PERFORM prismedia_refresh_entity_availability_targets(target_ids);
                    RETURN NULL;
                END;
                $function$;

                CREATE OR REPLACE FUNCTION prismedia_refresh_source_file_delete()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    target_ids uuid[];
                BEGIN
                    SELECT ARRAY_AGG(DISTINCT entity_id)
                    INTO target_ids
                    FROM old_rows
                    WHERE role = '{{sourceRole}}';
                    PERFORM prismedia_refresh_entity_availability_targets(target_ids);
                    RETURN NULL;
                END;
                $function$;

                CREATE OR REPLACE FUNCTION prismedia_refresh_entity_insert()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    target_ids uuid[];
                BEGIN
                    SELECT ARRAY_AGG(DISTINCT id) INTO target_ids FROM new_rows;
                    PERFORM prismedia_refresh_entity_availability_targets(target_ids);
                    RETURN NULL;
                END;
                $function$;

                CREATE OR REPLACE FUNCTION prismedia_refresh_entity_update()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    target_ids uuid[];
                BEGIN
                    SELECT ARRAY_AGG(DISTINCT entity_id)
                    INTO target_ids
                    FROM (
                        SELECT old_row.parent_entity_id AS entity_id
                        FROM old_rows AS old_row
                        INNER JOIN new_rows AS new_row ON new_row.id = old_row.id
                        WHERE old_row.parent_entity_id IS DISTINCT FROM new_row.parent_entity_id
                        UNION ALL
                        SELECT new_row.id
                        FROM old_rows AS old_row
                        INNER JOIN new_rows AS new_row ON new_row.id = old_row.id
                        WHERE old_row.parent_entity_id IS DISTINCT FROM new_row.parent_entity_id
                        UNION ALL
                        SELECT new_row.parent_entity_id
                        FROM old_rows AS old_row
                        INNER JOIN new_rows AS new_row ON new_row.id = old_row.id
                        WHERE old_row.parent_entity_id IS DISTINCT FROM new_row.parent_entity_id
                    ) AS targets
                    WHERE entity_id IS NOT NULL;
                    PERFORM prismedia_refresh_entity_availability_targets(target_ids);
                    RETURN NULL;
                END;
                $function$;

                CREATE OR REPLACE FUNCTION prismedia_refresh_entity_delete()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    target_ids uuid[];
                BEGIN
                    SELECT ARRAY_AGG(DISTINCT parent_entity_id)
                    INTO target_ids
                    FROM old_rows
                    WHERE parent_entity_id IS NOT NULL;
                    PERFORM prismedia_refresh_entity_availability_targets(target_ids);
                    RETURN NULL;
                END;
                $function$;

                CREATE TRIGGER prismedia_acquisition_availability_insert
                AFTER INSERT ON acquisitions
                REFERENCING NEW TABLE AS new_rows
                FOR EACH STATEMENT EXECUTE FUNCTION prismedia_refresh_acquisition_insert();
                CREATE TRIGGER prismedia_acquisition_availability_update
                AFTER UPDATE ON acquisitions
                REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows
                FOR EACH STATEMENT EXECUTE FUNCTION prismedia_refresh_acquisition_update();
                CREATE TRIGGER prismedia_acquisition_availability_delete
                AFTER DELETE ON acquisitions
                REFERENCING OLD TABLE AS old_rows
                FOR EACH STATEMENT EXECUTE FUNCTION prismedia_refresh_acquisition_delete();

                CREATE TRIGGER prismedia_source_file_availability_insert
                AFTER INSERT ON entity_files
                REFERENCING NEW TABLE AS new_rows
                FOR EACH STATEMENT EXECUTE FUNCTION prismedia_refresh_source_file_insert();
                CREATE TRIGGER prismedia_source_file_availability_update
                AFTER UPDATE ON entity_files
                REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows
                FOR EACH STATEMENT EXECUTE FUNCTION prismedia_refresh_source_file_update();
                CREATE TRIGGER prismedia_source_file_availability_delete
                AFTER DELETE ON entity_files
                REFERENCING OLD TABLE AS old_rows
                FOR EACH STATEMENT EXECUTE FUNCTION prismedia_refresh_source_file_delete();

                CREATE TRIGGER prismedia_entity_availability_insert
                AFTER INSERT ON entities
                REFERENCING NEW TABLE AS new_rows
                FOR EACH STATEMENT EXECUTE FUNCTION prismedia_refresh_entity_insert();
                CREATE TRIGGER prismedia_entity_availability_update
                AFTER UPDATE ON entities
                REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows
                FOR EACH STATEMENT EXECUTE FUNCTION prismedia_refresh_entity_update();
                CREATE TRIGGER prismedia_entity_availability_delete
                AFTER DELETE ON entities
                REFERENCING OLD TABLE AS old_rows
                FOR EACH STATEMENT EXECUTE FUNCTION prismedia_refresh_entity_delete();

                {{ReconcileCommand}}
                """;
        }
    }

    internal const string Drop = """
        DROP TRIGGER IF EXISTS prismedia_acquisition_availability_insert ON acquisitions;
        DROP TRIGGER IF EXISTS prismedia_acquisition_availability_update ON acquisitions;
        DROP TRIGGER IF EXISTS prismedia_acquisition_availability_delete ON acquisitions;
        DROP TRIGGER IF EXISTS prismedia_source_file_availability_insert ON entity_files;
        DROP TRIGGER IF EXISTS prismedia_source_file_availability_update ON entity_files;
        DROP TRIGGER IF EXISTS prismedia_source_file_availability_delete ON entity_files;
        DROP TRIGGER IF EXISTS prismedia_entity_availability_insert ON entities;
        DROP TRIGGER IF EXISTS prismedia_entity_availability_update ON entities;
        DROP TRIGGER IF EXISTS prismedia_entity_availability_delete ON entities;
        DROP FUNCTION IF EXISTS prismedia_refresh_acquisition_insert();
        DROP FUNCTION IF EXISTS prismedia_refresh_acquisition_update();
        DROP FUNCTION IF EXISTS prismedia_refresh_acquisition_delete();
        DROP FUNCTION IF EXISTS prismedia_refresh_source_file_insert();
        DROP FUNCTION IF EXISTS prismedia_refresh_source_file_update();
        DROP FUNCTION IF EXISTS prismedia_refresh_source_file_delete();
        DROP FUNCTION IF EXISTS prismedia_refresh_entity_insert();
        DROP FUNCTION IF EXISTS prismedia_refresh_entity_update();
        DROP FUNCTION IF EXISTS prismedia_refresh_entity_delete();
        DROP FUNCTION IF EXISTS prismedia_acquisition_target_entity(uuid);
        DROP FUNCTION IF EXISTS prismedia_reconcile_entity_availability();
        DROP FUNCTION IF EXISTS prismedia_refresh_entity_availability_targets(uuid[]);
        DROP FUNCTION IF EXISTS prismedia_refresh_entity_availability_roots(uuid[]);
        DROP INDEX IF EXISTS "IX_entity_availability_acquisition_status_codes";
        """;
}
