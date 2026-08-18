namespace Prismedia.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL-owned write projection for Entity rollups: per-entity inherited context
/// (effective library root and effective NSFW), structural descendant counts by kind,
/// inbound reference counts by source kind, and collection membership counts — everything the
/// card chips previously computed live with multi-level self-joins on every list page.
///
/// Count rows are keyed by the contributing entity's <em>effective library root</em>
/// (<see cref="NoRootSentinel"/> when none), so a viewer's chip is the sum over the roots they
/// may see and hidden-root entities never leak into a visible parent's badge. NSFW sub-counts
/// use the contributor's effective (inherited) NSFW flag, so hiding NSFW subtracts exactly the
/// entities the NSFW wall hides.
///
/// Maintenance recomputes affected nodes from their direct children's already-materialized
/// rows, deepest-first — bounded by children-per-node times tree depth, never a subtree walk —
/// which makes every refresh self-healing instead of drift-prone increment arithmetic. Only
/// inheritance changes (reparent, own-root assignment, NSFW flips) walk the affected subtree,
/// and those are rare interactive operations. Statement-level triggers collapse batch writes
/// into one refresh per statement; <c>prismedia_reconcile_entity_rollups()</c> is the worker's
/// idempotent full self-heal.
/// </summary>
internal static class EntityRollupProjectionSql {
    internal const string ReconcileCommand = "SELECT prismedia_reconcile_entity_rollups();";

    /// <summary>Root key used in count tables for contributors with no effective library root.</summary>
    internal static readonly Guid NoRootSentinel = Guid.Empty;

    internal const string Create = """
        CREATE INDEX "IX_entity_rollups_effective_library_root_id"
        ON entity_rollups (effective_library_root_id);

        -- Resolves inherited context (effective root, effective NSFW) for the subtrees rooted at
        -- scope_ids and upserts entity_rollups rows for every subtree member. Inherited context
        -- flows downward: each scope root first resolves its own context from its ancestors, then
        -- the walk descends. Returns the subtree member ids whose stored context CHANGED (their
        -- count rows are keyed by these values and must be re-keyed by the caller).
        CREATE OR REPLACE FUNCTION prismedia_refresh_entity_rollup_scopes(scope_ids uuid[])
        RETURNS uuid[]
        LANGUAGE plpgsql
        AS $function$
        DECLARE
            changed_ids uuid[];
        BEGIN
            IF scope_ids IS NULL OR cardinality(scope_ids) = 0 THEN
                RETURN ARRAY[]::uuid[];
            END IF;

            WITH RECURSIVE ancestor_context(scope_id, current_id, depth, path) AS (
                SELECT entity.id, entity.parent_entity_id, 1, ARRAY[entity.id]
                FROM entities AS entity
                WHERE entity.id = ANY (scope_ids)
                UNION ALL
                SELECT context.scope_id, parent.parent_entity_id, context.depth + 1, context.path || parent.id
                FROM ancestor_context AS context
                INNER JOIN entities AS parent ON parent.id = context.current_id
                WHERE NOT parent.id = ANY (context.path)
            ),
            -- Nearest own root above each scope entity (excluding the scope entity itself).
            inherited_root AS (
                SELECT DISTINCT ON (context.scope_id)
                    context.scope_id,
                    root.library_root_id
                FROM ancestor_context AS context
                INNER JOIN entity_library_roots AS root
                    ON root.entity_id = context.current_id AND root.library_root_id IS NOT NULL
                ORDER BY context.scope_id, context.depth
            ),
            inherited_nsfw AS (
                SELECT context.scope_id, BOOL_OR(ancestor.is_nsfw) AS is_nsfw
                FROM ancestor_context AS context
                INNER JOIN entities AS ancestor ON ancestor.id = context.current_id
                GROUP BY context.scope_id
            ),
            seed(entity_id, effective_root, effective_nsfw, path) AS (
                SELECT
                    entity.id,
                    COALESCE(own_root.library_root_id, inherited.library_root_id),
                    entity.is_nsfw OR COALESCE(nsfw.is_nsfw, FALSE),
                    ARRAY[entity.id]
                FROM entities AS entity
                LEFT JOIN entity_library_roots AS own_root ON own_root.entity_id = entity.id
                LEFT JOIN inherited_root AS inherited ON inherited.scope_id = entity.id
                LEFT JOIN inherited_nsfw AS nsfw ON nsfw.scope_id = entity.id
                WHERE entity.id = ANY (scope_ids)
            ),
            subtree(entity_id, effective_root, effective_nsfw, path) AS (
                SELECT entity_id, effective_root, effective_nsfw, path
                FROM seed
                UNION ALL
                SELECT
                    child.id,
                    COALESCE(own_root.library_root_id, subtree.effective_root),
                    child.is_nsfw OR subtree.effective_nsfw,
                    subtree.path || child.id
                FROM subtree
                INNER JOIN entities AS child ON child.parent_entity_id = subtree.entity_id
                LEFT JOIN entity_library_roots AS own_root ON own_root.entity_id = child.id
                WHERE NOT child.id = ANY (subtree.path)
            ),
            applied AS (
                INSERT INTO entity_rollups (
                    entity_id,
                    effective_library_root_id,
                    effective_is_nsfw,
                    direct_child_count,
                    latest_descendant_created_at,
                    updated_at)
                SELECT
                    subtree.entity_id,
                    subtree.effective_root,
                    subtree.effective_nsfw,
                    0,
                    NULL,
                    CURRENT_TIMESTAMP
                FROM subtree
                ON CONFLICT (entity_id) DO UPDATE
                SET effective_library_root_id = EXCLUDED.effective_library_root_id,
                    effective_is_nsfw = EXCLUDED.effective_is_nsfw,
                    updated_at = EXCLUDED.updated_at
                WHERE entity_rollups.effective_library_root_id IS DISTINCT FROM EXCLUDED.effective_library_root_id
                   OR entity_rollups.effective_is_nsfw IS DISTINCT FROM EXCLUDED.effective_is_nsfw
                RETURNING entity_rollups.entity_id
            )
            SELECT COALESCE(ARRAY_AGG(entity_id), ARRAY[]::uuid[])
            INTO changed_ids
            FROM applied;

            RETURN changed_ids;
        END;
        $function$;

        -- Recomputes descendant counts, direct child counts, and latest descendant timestamps for
        -- from_ids plus all of their ancestors, deepest level first, each node aggregated from its
        -- direct children's own facts and already-materialized descendant rows. Wanted placeholder
        -- children contribute no own row (matching list visibility); an NSFW child promotes its
        -- whole subtree into the parent's NSFW sub-count because the NSFW wall hides inherited.
        CREATE OR REPLACE FUNCTION prismedia_refresh_entity_rollup_counts(from_ids uuid[])
        RETURNS integer
        LANGUAGE plpgsql
        AS $function$
        DECLARE
            current_depth integer;
            max_depth integer;
            refreshed integer := 0;
            level_count integer;
        BEGIN
            IF from_ids IS NULL OR cardinality(from_ids) = 0 THEN
                RETURN 0;
            END IF;

            CREATE TEMP TABLE IF NOT EXISTS rollup_closure (
                entity_id uuid PRIMARY KEY,
                depth integer NOT NULL
            ) ON COMMIT DROP;
            DELETE FROM rollup_closure;

            -- Closure = from_ids and every ancestor; depth = distance from the top of each chain,
            -- so recomputing from max depth upward always reads fresh child rows.
            WITH RECURSIVE climb(entity_id, parent_entity_id, height, path) AS (
                SELECT entity.id, entity.parent_entity_id, 0, ARRAY[entity.id]
                FROM entities AS entity
                WHERE entity.id = ANY (from_ids)
                UNION ALL
                SELECT parent.id, parent.parent_entity_id, climb.height + 1, climb.path || parent.id
                FROM climb
                INNER JOIN entities AS parent ON parent.id = climb.parent_entity_id
                WHERE NOT parent.id = ANY (climb.path)
            ),
            member_heights AS (
                SELECT entity_id, MAX(height) AS height
                FROM climb
                GROUP BY entity_id
            ),
            chain_max AS (SELECT MAX(height) AS top FROM member_heights)
            INSERT INTO rollup_closure (entity_id, depth)
            SELECT member_heights.entity_id, chain_max.top - member_heights.height
            FROM member_heights, chain_max;

            SELECT MAX(depth) INTO max_depth FROM rollup_closure;
            IF max_depth IS NULL THEN
                RETURN 0;
            END IF;

            FOR current_depth IN REVERSE max_depth..0 LOOP
                WITH level AS (
                    SELECT entity_id FROM rollup_closure WHERE depth = current_depth
                ),
                contributions AS (
                    -- Each direct child contributes itself under its own kind and effective root…
                    SELECT
                        child.parent_entity_id AS entity_id,
                        child.kind_code AS descendant_kind_code,
                        COALESCE(context.effective_library_root_id, '00000000-0000-0000-0000-000000000000'::uuid) AS library_root_id,
                        COUNT(*) FILTER (WHERE NOT child.is_wanted) AS count_total,
                        COUNT(*) FILTER (WHERE NOT child.is_wanted AND COALESCE(context.effective_is_nsfw, child.is_nsfw)) AS count_nsfw
                    FROM entities AS child
                    INNER JOIN level ON level.entity_id = child.parent_entity_id
                    LEFT JOIN entity_rollups AS context ON context.entity_id = child.id
                    GROUP BY 1, 2, 3
                    UNION ALL
                    -- …and passes its own descendant rows upward, promoted to NSFW when the child
                    -- itself is effectively NSFW.
                    SELECT
                        child.parent_entity_id AS entity_id,
                        counts.descendant_kind_code,
                        counts.library_root_id,
                        SUM(counts.count_total) AS count_total,
                        SUM(CASE WHEN context.effective_is_nsfw THEN counts.count_total ELSE counts.count_nsfw END) AS count_nsfw
                    FROM entities AS child
                    INNER JOIN level ON level.entity_id = child.parent_entity_id
                    INNER JOIN entity_descendant_counts AS counts ON counts.entity_id = child.id
                    LEFT JOIN entity_rollups AS context ON context.entity_id = child.id
                    GROUP BY 1, 2, 3
                ),
                merged AS (
                    SELECT
                        entity_id,
                        descendant_kind_code,
                        library_root_id,
                        SUM(count_total)::integer AS count_total,
                        SUM(count_nsfw)::integer AS count_nsfw
                    FROM contributions
                    GROUP BY 1, 2, 3
                    HAVING SUM(count_total) > 0 OR SUM(count_nsfw) > 0
                ),
                pruned AS (
                    DELETE FROM entity_descendant_counts AS stale
                    USING level
                    WHERE stale.entity_id = level.entity_id
                      AND NOT EXISTS (
                        SELECT 1 FROM merged
                        WHERE merged.entity_id = stale.entity_id
                          AND merged.descendant_kind_code = stale.descendant_kind_code
                          AND merged.library_root_id = stale.library_root_id)
                    RETURNING stale.entity_id
                )
                INSERT INTO entity_descendant_counts (
                    entity_id, descendant_kind_code, library_root_id, count_total, count_nsfw)
                SELECT entity_id, descendant_kind_code, library_root_id, count_total, count_nsfw
                FROM merged
                ON CONFLICT (entity_id, descendant_kind_code, library_root_id) DO UPDATE
                SET count_total = EXCLUDED.count_total,
                    count_nsfw = EXCLUDED.count_nsfw
                WHERE entity_descendant_counts.count_total IS DISTINCT FROM EXCLUDED.count_total
                   OR entity_descendant_counts.count_nsfw IS DISTINCT FROM EXCLUDED.count_nsfw;

                GET DIAGNOSTICS level_count = ROW_COUNT;
                refreshed := refreshed + level_count;

                -- Direct child count and newest descendant timestamp ride the same level pass.
                -- (rollup_closure is read directly: the INSERT above is a separate statement, so
                -- its CTEs are out of scope here.)
                UPDATE entity_rollups AS rollup
                SET direct_child_count = COALESCE(children.direct_count, 0),
                    latest_descendant_created_at = children.latest,
                    updated_at = CURRENT_TIMESTAMP
                FROM (
                    SELECT
                        closure.entity_id,
                        COUNT(child.id) FILTER (WHERE NOT child.is_wanted) AS direct_count,
                        MAX(GREATEST(child.created_at, child_rollup.latest_descendant_created_at))
                            FILTER (WHERE NOT child.is_wanted) AS latest
                    FROM rollup_closure AS closure
                    LEFT JOIN entities AS child ON child.parent_entity_id = closure.entity_id
                    LEFT JOIN entity_rollups AS child_rollup ON child_rollup.entity_id = child.id
                    WHERE closure.depth = current_depth
                    GROUP BY closure.entity_id
                ) AS children
                WHERE rollup.entity_id = children.entity_id
                  AND (rollup.direct_child_count IS DISTINCT FROM COALESCE(children.direct_count, 0)
                    OR rollup.latest_descendant_created_at IS DISTINCT FROM children.latest);
            END LOOP;

            RETURN refreshed;
        END;
        $function$;

        -- Recomputes inbound reference counts (person/tag/studio chips and reference sorting) for
        -- the given target entities, grouped by source kind and the source's effective root.
        CREATE OR REPLACE FUNCTION prismedia_refresh_entity_reference_counts(target_ids uuid[])
        RETURNS integer
        LANGUAGE plpgsql
        AS $function$
        DECLARE
            refreshed integer;
        BEGIN
            IF target_ids IS NULL OR cardinality(target_ids) = 0 THEN
                RETURN 0;
            END IF;

            DELETE FROM entity_reference_counts WHERE entity_id = ANY (target_ids);
            INSERT INTO entity_reference_counts (
                entity_id, source_kind_code, library_root_id, count_total, count_nsfw)
            SELECT
                link.target_entity_id,
                source.kind_code,
                COALESCE(context.effective_library_root_id, '00000000-0000-0000-0000-000000000000'::uuid),
                COUNT(DISTINCT link.entity_id),
                COUNT(DISTINCT link.entity_id) FILTER (WHERE COALESCE(context.effective_is_nsfw, source.is_nsfw))
            FROM entity_relationship_links AS link
            INNER JOIN entities AS source ON source.id = link.entity_id
            LEFT JOIN entity_rollups AS context ON context.entity_id = source.id
            WHERE link.target_entity_id = ANY (target_ids)
              AND NOT source.is_wanted
            GROUP BY 1, 2, 3;

            GET DIAGNOSTICS refreshed = ROW_COUNT;
            RETURN refreshed;
        END;
        $function$;

        -- Recomputes collection membership counts for the given collection entities, grouped by
        -- each member's effective root.
        CREATE OR REPLACE FUNCTION prismedia_refresh_entity_collection_counts(collection_ids uuid[])
        RETURNS integer
        LANGUAGE plpgsql
        AS $function$
        DECLARE
            refreshed integer;
        BEGIN
            IF collection_ids IS NULL OR cardinality(collection_ids) = 0 THEN
                RETURN 0;
            END IF;

            DELETE FROM entity_collection_member_counts WHERE entity_id = ANY (collection_ids);
            INSERT INTO entity_collection_member_counts (
                entity_id, library_root_id, count_total, count_nsfw)
            SELECT
                item.collection_entity_id,
                COALESCE(context.effective_library_root_id, '00000000-0000-0000-0000-000000000000'::uuid),
                COUNT(DISTINCT item.item_entity_id),
                COUNT(DISTINCT item.item_entity_id) FILTER (WHERE COALESCE(context.effective_is_nsfw, member.is_nsfw))
            FROM collection_item_details AS item
            INNER JOIN entities AS member ON member.id = item.item_entity_id
            LEFT JOIN entity_rollups AS context ON context.entity_id = member.id
            WHERE item.collection_entity_id = ANY (collection_ids)
            GROUP BY 1, 2;

            GET DIAGNOSTICS refreshed = ROW_COUNT;
            RETURN refreshed;
        END;
        $function$;

        -- Main refresh entry: resolves inherited context for the changed subtrees, then recomputes
        -- counts for every node whose aggregation inputs may have changed (changed ids, re-keyed
        -- subtree members, and all of their ancestors), and re-keys reference/collection rows fed
        -- by re-keyed sources. For ordinary leaf churn (scan inserts, deletes) the subtree walk
        -- degenerates to the changed rows themselves and only the ancestor chains recompute.
        CREATE OR REPLACE FUNCTION prismedia_refresh_entity_rollups_targets(changed_ids uuid[])
        RETURNS integer
        LANGUAGE plpgsql
        AS $function$
        DECLARE
            rekeyed_ids uuid[];
            count_seed_ids uuid[];
            reference_target_ids uuid[];
            collection_ids uuid[];
        BEGIN
            IF changed_ids IS NULL OR cardinality(changed_ids) = 0 THEN
                RETURN 0;
            END IF;

            rekeyed_ids := prismedia_refresh_entity_rollup_scopes(changed_ids);

            SELECT COALESCE(ARRAY_AGG(DISTINCT id), ARRAY[]::uuid[])
            INTO count_seed_ids
            FROM unnest(changed_ids || rekeyed_ids) AS id;

            PERFORM prismedia_refresh_entity_rollup_counts(count_seed_ids);

            SELECT COALESCE(ARRAY_AGG(DISTINCT link.target_entity_id), ARRAY[]::uuid[])
            INTO reference_target_ids
            FROM entity_relationship_links AS link
            WHERE link.entity_id = ANY (count_seed_ids);
            PERFORM prismedia_refresh_entity_reference_counts(reference_target_ids);

            SELECT COALESCE(ARRAY_AGG(DISTINCT item.collection_entity_id), ARRAY[]::uuid[])
            INTO collection_ids
            FROM collection_item_details AS item
            WHERE item.item_entity_id = ANY (count_seed_ids);
            PERFORM prismedia_refresh_entity_collection_counts(collection_ids);

            RETURN cardinality(count_seed_ids);
        END;
        $function$;

        -- Full self-heal: recompute inherited context for every top-level subtree, then every
        -- count table from scratch, and drop rollup rows for entities that no longer exist
        -- (cascade normally handles this; reconcile repairs any historical drift).
        CREATE OR REPLACE FUNCTION prismedia_reconcile_entity_rollups()
        RETURNS integer
        LANGUAGE plpgsql
        AS $function$
        DECLARE
            top_level_ids uuid[];
            all_parent_ids uuid[];
            all_target_ids uuid[];
            all_collection_ids uuid[];
            repaired integer := 0;
        BEGIN
            SELECT COALESCE(ARRAY_AGG(id), ARRAY[]::uuid[])
            INTO top_level_ids
            FROM entities
            WHERE parent_entity_id IS NULL;
            repaired := repaired + COALESCE(cardinality(prismedia_refresh_entity_rollup_scopes(top_level_ids)), 0);

            SELECT COALESCE(ARRAY_AGG(DISTINCT parent_entity_id), ARRAY[]::uuid[])
            INTO all_parent_ids
            FROM entities
            WHERE parent_entity_id IS NOT NULL;
            repaired := repaired + prismedia_refresh_entity_rollup_counts(all_parent_ids);

            SELECT COALESCE(ARRAY_AGG(DISTINCT target_entity_id), ARRAY[]::uuid[])
            INTO all_target_ids
            FROM entity_relationship_links;
            repaired := repaired + prismedia_refresh_entity_reference_counts(all_target_ids);

            SELECT COALESCE(ARRAY_AGG(DISTINCT collection_entity_id), ARRAY[]::uuid[])
            INTO all_collection_ids
            FROM collection_item_details;
            repaired := repaired + prismedia_refresh_entity_collection_counts(all_collection_ids);

            -- Count rows whose owner lost every child linger only until this pass; clear them.
            DELETE FROM entity_descendant_counts AS counts
            WHERE NOT EXISTS (
                SELECT 1 FROM entities AS child WHERE child.parent_entity_id = counts.entity_id);
            DELETE FROM entity_reference_counts AS counts
            WHERE NOT EXISTS (
                SELECT 1 FROM entity_relationship_links AS link WHERE link.target_entity_id = counts.entity_id);
            DELETE FROM entity_collection_member_counts AS counts
            WHERE NOT EXISTS (
                SELECT 1 FROM collection_item_details AS item WHERE item.collection_entity_id = counts.entity_id);

            RETURN repaired;
        END;
        $function$;

        CREATE OR REPLACE FUNCTION prismedia_rollup_entity_insert()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $function$
        DECLARE
            target_ids uuid[];
        BEGIN
            SELECT ARRAY_AGG(DISTINCT id) INTO target_ids FROM new_rows;
            PERFORM prismedia_refresh_entity_rollups_targets(target_ids);
            RETURN NULL;
        END;
        $function$;

        CREATE OR REPLACE FUNCTION prismedia_rollup_entity_update()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $function$
        DECLARE
            target_ids uuid[];
        BEGIN
            -- Only structural or classification changes affect rollups. Old parents join the set
            -- so a reparent decrements the chain the entity left.
            SELECT ARRAY_AGG(DISTINCT entity_id)
            INTO target_ids
            FROM (
                SELECT new_row.id AS entity_id
                FROM old_rows AS old_row
                INNER JOIN new_rows AS new_row ON new_row.id = old_row.id
                WHERE old_row.parent_entity_id IS DISTINCT FROM new_row.parent_entity_id
                   OR old_row.is_nsfw IS DISTINCT FROM new_row.is_nsfw
                   OR old_row.is_wanted IS DISTINCT FROM new_row.is_wanted
                   OR old_row.kind_code IS DISTINCT FROM new_row.kind_code
                UNION ALL
                SELECT old_row.parent_entity_id
                FROM old_rows AS old_row
                INNER JOIN new_rows AS new_row ON new_row.id = old_row.id
                WHERE old_row.parent_entity_id IS DISTINCT FROM new_row.parent_entity_id
                  AND old_row.parent_entity_id IS NOT NULL
            ) AS targets
            WHERE entity_id IS NOT NULL;
            PERFORM prismedia_refresh_entity_rollups_targets(target_ids);
            RETURN NULL;
        END;
        $function$;

        CREATE OR REPLACE FUNCTION prismedia_rollup_entity_delete()
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
            PERFORM prismedia_refresh_entity_rollups_targets(target_ids);
            RETURN NULL;
        END;
        $function$;

        CREATE OR REPLACE FUNCTION prismedia_rollup_entity_root_change()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $function$
        DECLARE
            target_ids uuid[];
        BEGIN
            IF TG_OP = 'DELETE' THEN
                SELECT ARRAY_AGG(DISTINCT entity_id) INTO target_ids FROM old_rows;
            ELSIF TG_OP = 'UPDATE' THEN
                SELECT ARRAY_AGG(DISTINCT entity_id)
                INTO target_ids
                FROM (
                    SELECT new_row.entity_id
                    FROM old_rows AS old_row
                    INNER JOIN new_rows AS new_row ON new_row.entity_id = old_row.entity_id
                    WHERE old_row.library_root_id IS DISTINCT FROM new_row.library_root_id
                ) AS targets;
            ELSE
                SELECT ARRAY_AGG(DISTINCT entity_id) INTO target_ids FROM new_rows;
            END IF;
            PERFORM prismedia_refresh_entity_rollups_targets(target_ids);
            RETURN NULL;
        END;
        $function$;

        CREATE OR REPLACE FUNCTION prismedia_rollup_link_change()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $function$
        DECLARE
            target_ids uuid[];
        BEGIN
            IF TG_OP = 'DELETE' THEN
                SELECT ARRAY_AGG(DISTINCT target_entity_id) INTO target_ids FROM old_rows;
            ELSIF TG_OP = 'UPDATE' THEN
                SELECT ARRAY_AGG(DISTINCT target_entity_id)
                INTO target_ids
                FROM (
                    SELECT target_entity_id FROM new_rows
                    UNION ALL
                    SELECT target_entity_id FROM old_rows
                ) AS targets;
            ELSE
                SELECT ARRAY_AGG(DISTINCT target_entity_id) INTO target_ids FROM new_rows;
            END IF;
            PERFORM prismedia_refresh_entity_reference_counts(target_ids);
            RETURN NULL;
        END;
        $function$;

        CREATE OR REPLACE FUNCTION prismedia_rollup_collection_item_change()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $function$
        DECLARE
            target_ids uuid[];
        BEGIN
            IF TG_OP = 'DELETE' THEN
                SELECT ARRAY_AGG(DISTINCT collection_entity_id) INTO target_ids FROM old_rows;
            ELSIF TG_OP = 'UPDATE' THEN
                SELECT ARRAY_AGG(DISTINCT collection_entity_id)
                INTO target_ids
                FROM (
                    SELECT collection_entity_id FROM new_rows
                    UNION ALL
                    SELECT collection_entity_id FROM old_rows
                ) AS targets;
            ELSE
                SELECT ARRAY_AGG(DISTINCT collection_entity_id) INTO target_ids FROM new_rows;
            END IF;
            PERFORM prismedia_refresh_entity_collection_counts(target_ids);
            RETURN NULL;
        END;
        $function$;

        CREATE TRIGGER prismedia_entity_rollups_insert
        AFTER INSERT ON entities
        REFERENCING NEW TABLE AS new_rows
        FOR EACH STATEMENT EXECUTE FUNCTION prismedia_rollup_entity_insert();
        CREATE TRIGGER prismedia_entity_rollups_update
        AFTER UPDATE ON entities
        REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows
        FOR EACH STATEMENT EXECUTE FUNCTION prismedia_rollup_entity_update();
        CREATE TRIGGER prismedia_entity_rollups_delete
        AFTER DELETE ON entities
        REFERENCING OLD TABLE AS old_rows
        FOR EACH STATEMENT EXECUTE FUNCTION prismedia_rollup_entity_delete();

        CREATE TRIGGER prismedia_entity_rollups_root_insert
        AFTER INSERT ON entity_library_roots
        REFERENCING NEW TABLE AS new_rows
        FOR EACH STATEMENT EXECUTE FUNCTION prismedia_rollup_entity_root_change();
        CREATE TRIGGER prismedia_entity_rollups_root_update
        AFTER UPDATE ON entity_library_roots
        REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows
        FOR EACH STATEMENT EXECUTE FUNCTION prismedia_rollup_entity_root_change();
        CREATE TRIGGER prismedia_entity_rollups_root_delete
        AFTER DELETE ON entity_library_roots
        REFERENCING OLD TABLE AS old_rows
        FOR EACH STATEMENT EXECUTE FUNCTION prismedia_rollup_entity_root_change();

        CREATE TRIGGER prismedia_entity_rollups_link_insert
        AFTER INSERT ON entity_relationship_links
        REFERENCING NEW TABLE AS new_rows
        FOR EACH STATEMENT EXECUTE FUNCTION prismedia_rollup_link_change();
        CREATE TRIGGER prismedia_entity_rollups_link_update
        AFTER UPDATE ON entity_relationship_links
        REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows
        FOR EACH STATEMENT EXECUTE FUNCTION prismedia_rollup_link_change();
        CREATE TRIGGER prismedia_entity_rollups_link_delete
        AFTER DELETE ON entity_relationship_links
        REFERENCING OLD TABLE AS old_rows
        FOR EACH STATEMENT EXECUTE FUNCTION prismedia_rollup_link_change();

        CREATE TRIGGER prismedia_entity_rollups_collection_insert
        AFTER INSERT ON collection_item_details
        REFERENCING NEW TABLE AS new_rows
        FOR EACH STATEMENT EXECUTE FUNCTION prismedia_rollup_collection_item_change();
        CREATE TRIGGER prismedia_entity_rollups_collection_update
        AFTER UPDATE ON collection_item_details
        REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows
        FOR EACH STATEMENT EXECUTE FUNCTION prismedia_rollup_collection_item_change();
        CREATE TRIGGER prismedia_entity_rollups_collection_delete
        AFTER DELETE ON collection_item_details
        REFERENCING OLD TABLE AS old_rows
        FOR EACH STATEMENT EXECUTE FUNCTION prismedia_rollup_collection_item_change();

        SELECT prismedia_reconcile_entity_rollups();
        """;

    /// <summary>
    /// Replaces the reference/collection count refresh bodies with guarded upserts plus stale-row
    /// pruning. The original delete-and-reinsert shape rewrote every row on each refresh, which
    /// churned identical rows and inflated the reconcile function's repaired-row count — the
    /// 6-hourly self-heal would have warned about tens of thousands of "repairs" that were
    /// byte-identical rewrites. With the guards, the repaired count reports only real drift.
    /// Applied by a follow-up migration; the original creation SQL above stays byte-stable
    /// because its migration is published history.
    /// </summary>
    internal const string GuardedCountRefresh = """
        CREATE OR REPLACE FUNCTION prismedia_refresh_entity_reference_counts(target_ids uuid[])
        RETURNS integer
        LANGUAGE plpgsql
        AS $function$
        DECLARE
            refreshed integer;
        BEGIN
            IF target_ids IS NULL OR cardinality(target_ids) = 0 THEN
                RETURN 0;
            END IF;

            WITH fresh AS (
                SELECT
                    link.target_entity_id AS entity_id,
                    source.kind_code AS source_kind_code,
                    COALESCE(context.effective_library_root_id, '00000000-0000-0000-0000-000000000000'::uuid) AS library_root_id,
                    COUNT(DISTINCT link.entity_id)::integer AS count_total,
                    (COUNT(DISTINCT link.entity_id) FILTER (WHERE COALESCE(context.effective_is_nsfw, source.is_nsfw)))::integer AS count_nsfw
                FROM entity_relationship_links AS link
                INNER JOIN entities AS source ON source.id = link.entity_id
                LEFT JOIN entity_rollups AS context ON context.entity_id = source.id
                WHERE link.target_entity_id = ANY (target_ids)
                  AND NOT source.is_wanted
                GROUP BY 1, 2, 3
            ),
            upserted AS (
                INSERT INTO entity_reference_counts (
                    entity_id, source_kind_code, library_root_id, count_total, count_nsfw)
                SELECT entity_id, source_kind_code, library_root_id, count_total, count_nsfw
                FROM fresh
                ON CONFLICT (entity_id, source_kind_code, library_root_id) DO UPDATE
                SET count_total = EXCLUDED.count_total,
                    count_nsfw = EXCLUDED.count_nsfw
                WHERE entity_reference_counts.count_total IS DISTINCT FROM EXCLUDED.count_total
                   OR entity_reference_counts.count_nsfw IS DISTINCT FROM EXCLUDED.count_nsfw
                RETURNING 1
            ),
            stale AS (
                DELETE FROM entity_reference_counts AS counts
                WHERE counts.entity_id = ANY (target_ids)
                  AND NOT EXISTS (
                    SELECT 1 FROM fresh
                    WHERE fresh.entity_id = counts.entity_id
                      AND fresh.source_kind_code = counts.source_kind_code
                      AND fresh.library_root_id = counts.library_root_id)
                RETURNING 1
            )
            SELECT (SELECT COUNT(*) FROM upserted) + (SELECT COUNT(*) FROM stale)
            INTO refreshed;
            RETURN refreshed;
        END;
        $function$;

        CREATE OR REPLACE FUNCTION prismedia_refresh_entity_collection_counts(collection_ids uuid[])
        RETURNS integer
        LANGUAGE plpgsql
        AS $function$
        DECLARE
            refreshed integer;
        BEGIN
            IF collection_ids IS NULL OR cardinality(collection_ids) = 0 THEN
                RETURN 0;
            END IF;

            WITH fresh AS (
                SELECT
                    item.collection_entity_id AS entity_id,
                    COALESCE(context.effective_library_root_id, '00000000-0000-0000-0000-000000000000'::uuid) AS library_root_id,
                    COUNT(DISTINCT item.item_entity_id)::integer AS count_total,
                    (COUNT(DISTINCT item.item_entity_id) FILTER (WHERE COALESCE(context.effective_is_nsfw, member.is_nsfw)))::integer AS count_nsfw
                FROM collection_item_details AS item
                INNER JOIN entities AS member ON member.id = item.item_entity_id
                LEFT JOIN entity_rollups AS context ON context.entity_id = member.id
                WHERE item.collection_entity_id = ANY (collection_ids)
                GROUP BY 1, 2
            ),
            upserted AS (
                INSERT INTO entity_collection_member_counts (
                    entity_id, library_root_id, count_total, count_nsfw)
                SELECT entity_id, library_root_id, count_total, count_nsfw
                FROM fresh
                ON CONFLICT (entity_id, library_root_id) DO UPDATE
                SET count_total = EXCLUDED.count_total,
                    count_nsfw = EXCLUDED.count_nsfw
                WHERE entity_collection_member_counts.count_total IS DISTINCT FROM EXCLUDED.count_total
                   OR entity_collection_member_counts.count_nsfw IS DISTINCT FROM EXCLUDED.count_nsfw
                RETURNING 1
            ),
            stale AS (
                DELETE FROM entity_collection_member_counts AS counts
                WHERE counts.entity_id = ANY (collection_ids)
                  AND NOT EXISTS (
                    SELECT 1 FROM fresh
                    WHERE fresh.entity_id = counts.entity_id
                      AND fresh.library_root_id = counts.library_root_id)
                RETURNING 1
            )
            SELECT (SELECT COUNT(*) FROM upserted) + (SELECT COUNT(*) FROM stale)
            INTO refreshed;
            RETURN refreshed;
        END;
        $function$;
        """;

    internal const string Drop = """
        DROP TRIGGER IF EXISTS prismedia_entity_rollups_insert ON entities;
        DROP TRIGGER IF EXISTS prismedia_entity_rollups_update ON entities;
        DROP TRIGGER IF EXISTS prismedia_entity_rollups_delete ON entities;
        DROP TRIGGER IF EXISTS prismedia_entity_rollups_root_insert ON entity_library_roots;
        DROP TRIGGER IF EXISTS prismedia_entity_rollups_root_update ON entity_library_roots;
        DROP TRIGGER IF EXISTS prismedia_entity_rollups_root_delete ON entity_library_roots;
        DROP TRIGGER IF EXISTS prismedia_entity_rollups_link_insert ON entity_relationship_links;
        DROP TRIGGER IF EXISTS prismedia_entity_rollups_link_update ON entity_relationship_links;
        DROP TRIGGER IF EXISTS prismedia_entity_rollups_link_delete ON entity_relationship_links;
        DROP TRIGGER IF EXISTS prismedia_entity_rollups_collection_insert ON collection_item_details;
        DROP TRIGGER IF EXISTS prismedia_entity_rollups_collection_update ON collection_item_details;
        DROP TRIGGER IF EXISTS prismedia_entity_rollups_collection_delete ON collection_item_details;
        DROP FUNCTION IF EXISTS prismedia_rollup_entity_insert();
        DROP FUNCTION IF EXISTS prismedia_rollup_entity_update();
        DROP FUNCTION IF EXISTS prismedia_rollup_entity_delete();
        DROP FUNCTION IF EXISTS prismedia_rollup_entity_root_change();
        DROP FUNCTION IF EXISTS prismedia_rollup_link_change();
        DROP FUNCTION IF EXISTS prismedia_rollup_collection_item_change();
        DROP FUNCTION IF EXISTS prismedia_reconcile_entity_rollups();
        DROP FUNCTION IF EXISTS prismedia_refresh_entity_rollups_targets(uuid[]);
        DROP FUNCTION IF EXISTS prismedia_refresh_entity_collection_counts(uuid[]);
        DROP FUNCTION IF EXISTS prismedia_refresh_entity_reference_counts(uuid[]);
        DROP FUNCTION IF EXISTS prismedia_refresh_entity_rollup_counts(uuid[]);
        DROP FUNCTION IF EXISTS prismedia_refresh_entity_rollup_scopes(uuid[]);
        DROP INDEX IF EXISTS "IX_entity_rollups_effective_library_root_id";
        """;
}
