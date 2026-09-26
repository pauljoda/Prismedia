using Npgsql;
using Prismedia.Domain.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Migrations;

public partial class AddConnectedIntegrations {
    #region Static Variables

    /// <summary>Native acquisition statuses that keep a work and scope from connected owners.</summary>
    private static string OwningAcquisitionStatuses => SqlList(AcquisitionStatusDefinition.OwningFulfillment);

    /// <summary>Native acquisition statuses that assert ownership when an acquisition enters them.</summary>
    private static string ClaimingAcquisitionStatuses => SqlList(AcquisitionStatusDefinition.ClaimingFulfillment);

    /// <summary>Monitor statuses that are tearing a monitor down and never assert ownership.</summary>
    private static string ReleasingMonitorStatuses => SqlList([MonitorStatus.Stopping, MonitorStatus.DeletingFiles]);

    // Every ownership check serializes on one transaction lock so a reservation and a native claim created
    // concurrently cannot both miss each other. The guards therefore return before that lock whenever the
    // change cannot assert a new claim: a release, a terminal or teardown status, an existing claim that
    // keeps its scope, or an identity row that was only touched. Functions are VOLATILE so a statement
    // waiting on the lock reads a fresh committed snapshot after the previous owner commits.
    private static string OwnershipGuards => $$"""
        CREATE FUNCTION prismedia_fulfillment_equivalent(a uuid, b uuid, depth integer DEFAULT 0)
        RETURNS boolean LANGUAGE plpgsql VOLATILE AS $fn$
        DECLARE x entities; y entities; position_code text;
        BEGIN
            IF a IS NULL OR b IS NULL OR depth > 32 THEN RETURN false; END IF;
            IF a = b THEN RETURN true; END IF;
            SELECT * INTO x FROM entities WHERE id = a;
            SELECT * INTO y FROM entities WHERE id = b;
            IF x.kind_code IS DISTINCT FROM y.kind_code THEN RETURN false; END IF;
            IF EXISTS (SELECT 1 FROM entity_external_ids i JOIN entity_external_ids j
                ON lower(i.provider) = lower(j.provider) AND i.value = j.value
                WHERE i.entity_id = a AND j.entity_id = b) THEN RETURN true; END IF;
            position_code := CASE x.kind_code
                WHEN '{{EntityKind.VideoSeason.ToCode()}}' THEN '{{EntityPositionCodes.Season}}'
                WHEN '{{EntityKind.VideoEpisode.ToCode()}}' THEN '{{EntityPositionCodes.Episode}}'
                ELSE NULL END;
            RETURN position_code IS NOT NULL AND EXISTS (SELECT 1 FROM entity_positions i JOIN entity_positions j
                ON i.code = j.code AND i.value = j.value WHERE i.entity_id = a AND j.entity_id = b
                AND i.code = position_code)
                AND prismedia_fulfillment_equivalent(x.parent_entity_id, y.parent_entity_id, depth + 1);
        END $fn$;

        CREATE FUNCTION prismedia_fulfillment_overlap(a uuid, b uuid)
        RETURNS boolean LANGUAGE sql VOLATILE AS $fn$
            WITH RECURSIVE ancestors_a(id, parent_entity_id) AS (
                SELECT id, parent_entity_id FROM entities WHERE id = a
                UNION SELECT e.id, e.parent_entity_id FROM entities e JOIN ancestors_a p ON e.id = p.parent_entity_id
            ), ancestors_b(id, parent_entity_id) AS (
                SELECT id, parent_entity_id FROM entities WHERE id = b
                UNION SELECT e.id, e.parent_entity_id FROM entities e JOIN ancestors_b p ON e.id = p.parent_entity_id
            ) SELECT EXISTS (SELECT 1 FROM ancestors_a WHERE prismedia_fulfillment_equivalent(id, b))
                OR EXISTS (SELECT 1 FROM ancestors_b WHERE prismedia_fulfillment_equivalent(a, id));
        $fn$;

        CREATE FUNCTION prismedia_fulfillment_matches(r fulfillment_reservations, target uuid,
            target_kind text, rendition text, identity_namespace text DEFAULT NULL, identity_value text DEFAULT NULL)
        RETURNS boolean LANGUAGE sql VOLATILE AS $fn$
            SELECT (r.book_rendition IS NULL OR rendition IS NULL OR r.book_rendition = rendition)
                AND (prismedia_fulfillment_overlap(r.entity_id, target)
                    OR EXISTS (SELECT 1 FROM entities e WHERE e.id = r.entity_id AND e.kind_code = target_kind
                        AND ((identity_namespace IS NOT NULL AND r.external_ids ->> lower(identity_namespace) = identity_value)
                            OR EXISTS (SELECT 1 FROM entity_external_ids i WHERE i.entity_id = target
                                AND r.external_ids ->> lower(i.provider) = i.value))));
        $fn$;

        CREATE FUNCTION prismedia_fulfillment_matches_acquisition(r fulfillment_reservations, acquisition acquisitions)
        RETURNS boolean LANGUAGE plpgsql VOLATILE AS $fn$
        DECLARE current_acquisition acquisitions := acquisition; visited uuid[] := '{}'; rendition text;
        BEGIN
            rendition := CASE WHEN acquisition.kind = '{{EntityKind.Book.ToCode()}}'
                THEN coalesce(acquisition.book_rendition, '{{BookRendition.Ebook.ToCode()}}') ELSE NULL END;
            WHILE current_acquisition.id IS NOT NULL AND NOT current_acquisition.id = ANY(visited)
                AND cardinality(visited) < 32 LOOP
                IF prismedia_fulfillment_matches(r, current_acquisition.entity_id, acquisition.kind, rendition,
                    current_acquisition.identity_namespace, current_acquisition.identity_value) THEN RETURN true; END IF;
                visited := array_append(visited, current_acquisition.id);
                SELECT * INTO current_acquisition FROM acquisitions WHERE id = coalesce(
                    current_acquisition.upgrade_of_acquisition_id, current_acquisition.recovery_of_acquisition_id);
            END LOOP;
            RETURN false;
        END $fn$;

        CREATE FUNCTION prismedia_check_external_fulfillment(r fulfillment_reservations)
        RETURNS void LANGUAGE plpgsql VOLATILE AS $fn$
        DECLARE kind text;
        BEGIN
            SELECT kind_code INTO kind FROM entities WHERE id = r.entity_id;
            IF EXISTS (SELECT 1 FROM fulfillment_reservations other
                    WHERE other.released_at IS NULL AND other.id <> r.id
                    AND (other.owner_id <> r.owner_id OR other.owner_kind <> r.owner_kind OR other.connection_id <> r.connection_id)
                    AND (prismedia_fulfillment_matches(other, r.entity_id, kind, r.book_rendition)
                        OR prismedia_fulfillment_matches(r, other.entity_id,
                            (SELECT kind_code FROM entities WHERE id = other.entity_id), other.book_rendition)))
                OR EXISTS (SELECT 1 FROM acquisitions a
                    WHERE a.status IN ({{OwningAcquisitionStatuses}})
                    AND prismedia_fulfillment_matches_acquisition(r, a))
                OR EXISTS (SELECT 1 FROM monitors m LEFT JOIN acquisitions a ON a.id = m.acquisition_id
                    WHERE prismedia_fulfillment_matches(r, m.entity_id, m.kind,
                        CASE WHEN m.kind = '{{EntityKind.Book.ToCode()}}' THEN coalesce(m.book_rendition, '{{BookRendition.Ebook.ToCode()}}') ELSE NULL END)
                        OR prismedia_fulfillment_matches_acquisition(r, a))
            THEN RAISE EXCEPTION 'This work and scope already has an acquisition owner. Resolve the existing owner before delegating it.'
                USING ERRCODE = '{{PostgresErrorCodes.CheckViolation}}', CONSTRAINT = '{{FulfillmentOwnershipViolation.ConstraintName}}'; END IF;
        END $fn$;

        -- An accepted scope is immutable and a release is final, so an update never asserts a new claim.
        CREATE FUNCTION prismedia_guard_external_fulfillment_scope() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            IF NEW.owner_id <> OLD.owner_id OR NEW.owner_kind <> OLD.owner_kind
                OR NEW.entity_id <> OLD.entity_id OR NEW.connection_id <> OLD.connection_id
                OR NEW.book_rendition IS DISTINCT FROM OLD.book_rendition OR NEW.external_ids <> OLD.external_ids
                OR (OLD.released_at IS NOT NULL AND NEW.released_at IS DISTINCT FROM OLD.released_at)
            THEN RAISE EXCEPTION 'An accepted ownership scope is immutable.'
                USING ERRCODE = '{{PostgresErrorCodes.CheckViolation}}', CONSTRAINT = '{{FulfillmentOwnershipViolation.ConstraintName}}'; END IF;
            RETURN NEW;
        END $fn$;

        CREATE FUNCTION prismedia_guard_external_fulfillment() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            IF NEW.released_at IS NOT NULL THEN RETURN NEW; END IF;
            -- Match the FK lock before the ownership lock. Native lifecycle operations already hold
            -- their Entity row; taking these in reverse order would deadlock a concurrent acceptance.
            PERFORM id FROM entities WHERE id = NEW.entity_id FOR KEY SHARE;
            PERFORM id FROM integration_connections WHERE id = NEW.connection_id FOR KEY SHARE;
            PERFORM pg_advisory_xact_lock(5087441905);
            PERFORM prismedia_check_external_fulfillment(NEW);
            RETURN NEW;
        END $fn$;

        CREATE FUNCTION prismedia_guard_native_fulfillment() RETURNS trigger LANGUAGE plpgsql AS $fn$
        DECLARE rendition text; linked_acquisition acquisitions;
        BEGIN
            -- Leaving ownership, or keeping an existing claim over an unchanged scope, asserts nothing new.
            IF TG_TABLE_NAME = 'acquisitions' THEN
                IF NOT NEW.status IN ({{ClaimingAcquisitionStatuses}}) THEN RETURN NEW; END IF;
                IF TG_OP = 'UPDATE' AND OLD.status IN ({{ClaimingAcquisitionStatuses}})
                    AND NEW.entity_id IS NOT DISTINCT FROM OLD.entity_id AND NEW.kind IS NOT DISTINCT FROM OLD.kind
                    AND NEW.book_rendition IS NOT DISTINCT FROM OLD.book_rendition
                    AND NEW.identity_namespace IS NOT DISTINCT FROM OLD.identity_namespace
                    AND NEW.identity_value IS NOT DISTINCT FROM OLD.identity_value
                    AND NEW.upgrade_of_acquisition_id IS NOT DISTINCT FROM OLD.upgrade_of_acquisition_id
                    AND NEW.recovery_of_acquisition_id IS NOT DISTINCT FROM OLD.recovery_of_acquisition_id
                THEN RETURN NEW; END IF;
            ELSE
                IF NEW.status IN ({{ReleasingMonitorStatuses}}) THEN RETURN NEW; END IF;
                IF TG_OP = 'UPDATE' AND NOT OLD.status IN ({{ReleasingMonitorStatuses}})
                    AND NEW.entity_id IS NOT DISTINCT FROM OLD.entity_id
                    AND NEW.acquisition_id IS NOT DISTINCT FROM OLD.acquisition_id
                    AND NEW.kind IS NOT DISTINCT FROM OLD.kind AND NEW.book_rendition IS NOT DISTINCT FROM OLD.book_rendition
                THEN RETURN NEW; END IF;
            END IF;

            PERFORM id FROM entities WHERE id = NEW.entity_id FOR KEY SHARE;
            IF TG_TABLE_NAME = 'acquisitions' THEN
                PERFORM id FROM acquisitions WHERE id IN (NEW.upgrade_of_acquisition_id, NEW.recovery_of_acquisition_id) ORDER BY id FOR KEY SHARE;
            ELSE
                PERFORM id FROM acquisitions WHERE id = NEW.acquisition_id FOR KEY SHARE;
            END IF;
            PERFORM pg_advisory_xact_lock(5087441905);
            IF NOT EXISTS (SELECT 1 FROM fulfillment_reservations WHERE released_at IS NULL) THEN RETURN NEW; END IF;
            IF TG_TABLE_NAME = 'acquisitions' THEN
                IF EXISTS (SELECT 1 FROM fulfillment_reservations r WHERE r.released_at IS NULL
                    AND prismedia_fulfillment_matches_acquisition(r, NEW))
                THEN RAISE EXCEPTION 'A connected application owns this work and scope. Use its connection or complete an ownership handoff first.'
                    USING ERRCODE = '{{PostgresErrorCodes.CheckViolation}}', CONSTRAINT = '{{FulfillmentOwnershipViolation.ConstraintName}}'; END IF;
            ELSE
                SELECT * INTO linked_acquisition FROM acquisitions WHERE id = NEW.acquisition_id;
                rendition := CASE WHEN NEW.kind = '{{EntityKind.Book.ToCode()}}' THEN coalesce(NEW.book_rendition, '{{BookRendition.Ebook.ToCode()}}') ELSE NULL END;
                IF EXISTS (SELECT 1 FROM fulfillment_reservations r WHERE r.released_at IS NULL
                    AND (prismedia_fulfillment_matches(r, NEW.entity_id, NEW.kind, rendition)
                        OR prismedia_fulfillment_matches_acquisition(r, linked_acquisition)))
                THEN RAISE EXCEPTION 'A connected application owns this work and scope. Use its connection or complete an ownership handoff first.'
                    USING ERRCODE = '{{PostgresErrorCodes.CheckViolation}}', CONSTRAINT = '{{FulfillmentOwnershipViolation.ConstraintName}}'; END IF;
            END IF;
            RETURN NEW;
        END $fn$;

        CREATE TRIGGER fulfillment_reservation_guard BEFORE INSERT ON fulfillment_reservations
            FOR EACH ROW EXECUTE FUNCTION prismedia_guard_external_fulfillment();
        CREATE TRIGGER fulfillment_reservation_scope_guard BEFORE UPDATE ON fulfillment_reservations
            FOR EACH ROW EXECUTE FUNCTION prismedia_guard_external_fulfillment_scope();
        CREATE TRIGGER acquisition_fulfillment_guard BEFORE INSERT OR UPDATE OF entity_id, kind, book_rendition, status,
            identity_namespace, identity_value, upgrade_of_acquisition_id, recovery_of_acquisition_id ON acquisitions
            FOR EACH ROW EXECUTE FUNCTION prismedia_guard_native_fulfillment();
        CREATE TRIGGER monitor_fulfillment_guard BEFORE INSERT OR UPDATE OF entity_id, acquisition_id, kind, book_rendition, status ON monitors
            FOR EACH ROW EXECUTE FUNCTION prismedia_guard_native_fulfillment();

        -- A new or changed identity on one Entity can only make scopes in its own lineage (ancestors, itself
        -- and descendants) match something new. Recheck the active reservations in that lineage against
        -- every owner, and the native claims on that lineage (directly or through the acquisition they
        -- upgrade or recover) against every reservation. Removing an identity only removes matches, so
        -- deletes need no check, and a conflict elsewhere never blocks an unrelated identity change.
        CREATE FUNCTION prismedia_guard_fulfillment_identity() RETURNS trigger LANGUAGE plpgsql AS $fn$
        DECLARE changed uuid; lineage uuid[]; reservation fulfillment_reservations;
        BEGIN
            IF TG_TABLE_NAME = 'entities' THEN changed := NEW.id; ELSE changed := NEW.entity_id; END IF;
            PERFORM pg_advisory_xact_lock(5087441905);
            IF NOT EXISTS (SELECT 1 FROM fulfillment_reservations WHERE released_at IS NULL) THEN RETURN NULL; END IF;
            WITH RECURSIVE ancestors(id, parent_entity_id, depth) AS (
                SELECT id, parent_entity_id, 0 FROM entities WHERE id = changed
                UNION ALL SELECT e.id, e.parent_entity_id, a.depth + 1 FROM entities e
                    JOIN ancestors a ON e.id = a.parent_entity_id WHERE a.depth < 32
            ), descendants(id, depth) AS (
                SELECT id, 0 FROM entities WHERE id = changed
                UNION ALL SELECT e.id, d.depth + 1 FROM entities e
                    JOIN descendants d ON e.parent_entity_id = d.id WHERE d.depth < 32
            ) SELECT array_agg(DISTINCT id) INTO lineage FROM (SELECT id FROM ancestors UNION SELECT id FROM descendants) related;
            FOR reservation IN SELECT r.* FROM fulfillment_reservations r
                WHERE r.released_at IS NULL AND r.entity_id = ANY(lineage) LOOP
                PERFORM prismedia_check_external_fulfillment(reservation);
            END LOOP;
            IF EXISTS (SELECT 1 FROM acquisitions a WHERE a.status IN ({{OwningAcquisitionStatuses}})
                    AND (a.entity_id = ANY(lineage) OR EXISTS (SELECT 1 FROM acquisitions previous
                        WHERE previous.id IN (a.upgrade_of_acquisition_id, a.recovery_of_acquisition_id)
                        AND previous.entity_id = ANY(lineage)))
                    AND EXISTS (SELECT 1 FROM fulfillment_reservations r WHERE r.released_at IS NULL
                        AND prismedia_fulfillment_matches_acquisition(r, a)))
                OR EXISTS (SELECT 1 FROM monitors m LEFT JOIN acquisitions a ON a.id = m.acquisition_id
                    WHERE (m.entity_id = ANY(lineage) OR a.entity_id = ANY(lineage))
                    AND EXISTS (SELECT 1 FROM fulfillment_reservations r WHERE r.released_at IS NULL
                        AND (prismedia_fulfillment_matches(r, m.entity_id, m.kind,
                            CASE WHEN m.kind = '{{EntityKind.Book.ToCode()}}' THEN coalesce(m.book_rendition, '{{BookRendition.Ebook.ToCode()}}') ELSE NULL END)
                            OR prismedia_fulfillment_matches_acquisition(r, a))))
            THEN RAISE EXCEPTION 'This work and scope already has an acquisition owner. Resolve the existing owner before delegating it.'
                USING ERRCODE = '{{PostgresErrorCodes.CheckViolation}}', CONSTRAINT = '{{FulfillmentOwnershipViolation.ConstraintName}}'; END IF;
            RETURN NULL;
        END $fn$;
        CREATE TRIGGER entity_fulfillment_identity_guard AFTER UPDATE OF parent_entity_id, kind_code ON entities
            FOR EACH ROW WHEN (NEW.parent_entity_id IS DISTINCT FROM OLD.parent_entity_id OR NEW.kind_code IS DISTINCT FROM OLD.kind_code)
            EXECUTE FUNCTION prismedia_guard_fulfillment_identity();
        CREATE TRIGGER external_id_fulfillment_identity_guard AFTER INSERT ON entity_external_ids
            FOR EACH ROW EXECUTE FUNCTION prismedia_guard_fulfillment_identity();
        CREATE TRIGGER external_id_fulfillment_identity_change_guard AFTER UPDATE OF entity_id, provider, value ON entity_external_ids
            FOR EACH ROW WHEN (NEW.entity_id IS DISTINCT FROM OLD.entity_id OR NEW.provider IS DISTINCT FROM OLD.provider OR NEW.value IS DISTINCT FROM OLD.value)
            EXECUTE FUNCTION prismedia_guard_fulfillment_identity();
        CREATE TRIGGER position_fulfillment_identity_guard AFTER INSERT ON entity_positions
            FOR EACH ROW EXECUTE FUNCTION prismedia_guard_fulfillment_identity();
        CREATE TRIGGER position_fulfillment_identity_change_guard AFTER UPDATE OF entity_id, code, value ON entity_positions
            FOR EACH ROW WHEN (NEW.entity_id IS DISTINCT FROM OLD.entity_id OR NEW.code IS DISTINCT FROM OLD.code OR NEW.value IS DISTINCT FROM OLD.value)
            EXECUTE FUNCTION prismedia_guard_fulfillment_identity();
        """;

    private const string RemoveOwnershipGuards = """
        DROP TRIGGER IF EXISTS position_fulfillment_identity_change_guard ON entity_positions;
        DROP TRIGGER IF EXISTS position_fulfillment_identity_guard ON entity_positions;
        DROP TRIGGER IF EXISTS external_id_fulfillment_identity_change_guard ON entity_external_ids;
        DROP TRIGGER IF EXISTS external_id_fulfillment_identity_guard ON entity_external_ids;
        DROP TRIGGER IF EXISTS entity_fulfillment_identity_guard ON entities;
        DROP FUNCTION IF EXISTS prismedia_guard_fulfillment_identity();
        DROP TRIGGER IF EXISTS monitor_fulfillment_guard ON monitors;
        DROP TRIGGER IF EXISTS acquisition_fulfillment_guard ON acquisitions;
        DROP TRIGGER IF EXISTS fulfillment_reservation_scope_guard ON fulfillment_reservations;
        DROP TRIGGER IF EXISTS fulfillment_reservation_guard ON fulfillment_reservations;
        DROP FUNCTION IF EXISTS prismedia_guard_native_fulfillment();
        DROP FUNCTION IF EXISTS prismedia_guard_external_fulfillment();
        DROP FUNCTION IF EXISTS prismedia_guard_external_fulfillment_scope();
        DROP FUNCTION IF EXISTS prismedia_check_external_fulfillment(fulfillment_reservations);
        DROP FUNCTION IF EXISTS prismedia_fulfillment_matches_acquisition(fulfillment_reservations, acquisitions);
        DROP FUNCTION IF EXISTS prismedia_fulfillment_matches(fulfillment_reservations, uuid, text, text, text, text);
        DROP FUNCTION IF EXISTS prismedia_fulfillment_overlap(uuid, uuid);
        DROP FUNCTION IF EXISTS prismedia_fulfillment_equivalent(uuid, uuid, integer);
        """;

    #endregion

    #region Actions - SQL

    private static string SqlList<TCode>(IEnumerable<TCode> codes)
        where TCode : struct, Enum =>
        string.Join(", ", codes.Select(code => $"'{code.ToCode()}'"));

    #endregion
}
