using Npgsql;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Migrations;

public partial class AddFulfillmentReservations {
    private static string BackfillOwnership => $$"""
        WITH accepted_scopes AS (
            SELECT holding_id, entity_id FROM managed_source_bindings
            UNION
            SELECT holding.id, e.id FROM managed_holdings holding
                CROSS JOIN LATERAL jsonb_array_elements(holding.selections) selection
                JOIN entities e ON e.id::text = selection ->> 'entityId'
        )
        INSERT INTO fulfillment_reservations (id, owner_id, owner_kind, connection_id, entity_id, external_ids, created_at)
        SELECT gen_random_uuid(), scope.holding_id, '{{FulfillmentOwnerKind.ConnectedLibrary.ToCode()}}', holding.connection_id,
            scope.entity_id, coalesce((SELECT jsonb_object_agg(lower(provider), value)
                FROM entity_external_ids WHERE entity_id = scope.entity_id), '{}'::jsonb), now()
        FROM accepted_scopes scope JOIN managed_holdings holding ON holding.id = scope.holding_id;
        """;

    // Functions are VOLATILE so a statement waiting on the shared transaction lock reads a fresh
    // committed snapshot after the previous owner commits. Queue and network effects occur later.
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
                    WHERE a.status NOT IN ('{{AcquisitionStatus.Imported.ToCode()}}', '{{AcquisitionStatus.Cancelled.ToCode()}}')
                    AND prismedia_fulfillment_matches_acquisition(r, a))
                OR EXISTS (SELECT 1 FROM monitors m LEFT JOIN acquisitions a ON a.id = m.acquisition_id
                    WHERE prismedia_fulfillment_matches(r, m.entity_id, m.kind,
                        CASE WHEN m.kind = '{{EntityKind.Book.ToCode()}}' THEN coalesce(m.book_rendition, '{{BookRendition.Ebook.ToCode()}}') ELSE NULL END)
                        OR prismedia_fulfillment_matches_acquisition(r, a))
            THEN RAISE EXCEPTION 'This work and scope already has an acquisition owner. Resolve the existing owner before delegating it.'
                USING ERRCODE = '{{PostgresErrorCodes.CheckViolation}}', CONSTRAINT = '{{FulfillmentOwnershipViolation.ConstraintName}}'; END IF;
        END $fn$;

        CREATE FUNCTION prismedia_guard_external_fulfillment() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            -- Match the FK lock before the ownership lock. Native lifecycle operations already hold
            -- their Entity row; taking these in reverse order would deadlock a concurrent acceptance.
            PERFORM id FROM entities WHERE id = NEW.entity_id FOR KEY SHARE;
            PERFORM id FROM integration_connections WHERE id = NEW.connection_id FOR KEY SHARE;
            PERFORM pg_advisory_xact_lock(5087441905);
            IF TG_OP = 'UPDATE' AND (NEW.owner_id <> OLD.owner_id OR NEW.owner_kind <> OLD.owner_kind
                OR NEW.entity_id <> OLD.entity_id OR NEW.connection_id <> OLD.connection_id
                OR NEW.book_rendition IS DISTINCT FROM OLD.book_rendition OR NEW.external_ids <> OLD.external_ids
                OR (OLD.released_at IS NOT NULL AND NEW.released_at IS DISTINCT FROM OLD.released_at))
            THEN RAISE EXCEPTION 'An accepted ownership scope is immutable.'
                USING ERRCODE = '{{PostgresErrorCodes.CheckViolation}}', CONSTRAINT = '{{FulfillmentOwnershipViolation.ConstraintName}}'; END IF;
            IF NEW.released_at IS NULL THEN PERFORM prismedia_check_external_fulfillment(NEW); END IF;
            RETURN NEW;
        END $fn$;

        CREATE FUNCTION prismedia_guard_native_fulfillment() RETURNS trigger LANGUAGE plpgsql AS $fn$
        DECLARE rendition text; linked_acquisition acquisitions;
        BEGIN
            PERFORM id FROM entities WHERE id = NEW.entity_id FOR KEY SHARE;
            IF TG_TABLE_NAME = 'acquisitions' THEN
                PERFORM id FROM acquisitions WHERE id IN (NEW.upgrade_of_acquisition_id, NEW.recovery_of_acquisition_id) ORDER BY id FOR KEY SHARE;
            ELSE
                PERFORM id FROM acquisitions WHERE id = NEW.acquisition_id FOR KEY SHARE;
            END IF;
            PERFORM pg_advisory_xact_lock(5087441905);
            IF TG_TABLE_NAME = 'acquisitions' THEN
                IF NEW.status IN ('{{AcquisitionStatus.Imported.ToCode()}}', '{{AcquisitionStatus.Cancelled.ToCode()}}', '{{AcquisitionStatus.Stopping.ToCode()}}') THEN RETURN NEW; END IF;
                IF EXISTS (SELECT 1 FROM fulfillment_reservations r WHERE r.released_at IS NULL
                    AND prismedia_fulfillment_matches_acquisition(r, NEW))
                THEN RAISE EXCEPTION 'A connected application owns this work and scope. Use its connection or complete an ownership handoff first.'
                    USING ERRCODE = '{{PostgresErrorCodes.CheckViolation}}', CONSTRAINT = '{{FulfillmentOwnershipViolation.ConstraintName}}'; END IF;
            ELSE
                IF NEW.status IN ('{{MonitorStatus.Stopping.ToCode()}}', '{{MonitorStatus.DeletingFiles.ToCode()}}') THEN RETURN NEW; END IF;
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

        CREATE TRIGGER fulfillment_reservation_guard BEFORE INSERT OR UPDATE ON fulfillment_reservations
            FOR EACH ROW EXECUTE FUNCTION prismedia_guard_external_fulfillment();
        CREATE TRIGGER acquisition_fulfillment_guard BEFORE INSERT OR UPDATE OF entity_id, kind, book_rendition, status,
            identity_namespace, identity_value, upgrade_of_acquisition_id, recovery_of_acquisition_id ON acquisitions
            FOR EACH ROW EXECUTE FUNCTION prismedia_guard_native_fulfillment();
        CREATE TRIGGER monitor_fulfillment_guard BEFORE INSERT OR UPDATE OF entity_id, acquisition_id, kind, book_rendition, status ON monitors
            FOR EACH ROW EXECUTE FUNCTION prismedia_guard_native_fulfillment();

        CREATE FUNCTION prismedia_guard_fulfillment_identity() RETURNS trigger LANGUAGE plpgsql AS $fn$
        DECLARE reservation fulfillment_reservations;
        BEGIN
            PERFORM pg_advisory_xact_lock(5087441905);
            FOR reservation IN SELECT * FROM fulfillment_reservations WHERE released_at IS NULL LOOP
                PERFORM prismedia_check_external_fulfillment(reservation);
            END LOOP;
            RETURN NULL;
        END $fn$;
        CREATE TRIGGER entity_fulfillment_identity_guard AFTER UPDATE OF parent_entity_id, kind_code ON entities
            FOR EACH ROW EXECUTE FUNCTION prismedia_guard_fulfillment_identity();
        CREATE TRIGGER external_id_fulfillment_identity_guard AFTER INSERT OR UPDATE OR DELETE ON entity_external_ids
            FOR EACH ROW EXECUTE FUNCTION prismedia_guard_fulfillment_identity();
        CREATE TRIGGER position_fulfillment_identity_guard AFTER INSERT OR UPDATE OR DELETE ON entity_positions
            FOR EACH ROW EXECUTE FUNCTION prismedia_guard_fulfillment_identity();
        """;

    private const string RemoveOwnershipGuards = """
        DROP TRIGGER IF EXISTS position_fulfillment_identity_guard ON entity_positions;
        DROP TRIGGER IF EXISTS external_id_fulfillment_identity_guard ON entity_external_ids;
        DROP TRIGGER IF EXISTS entity_fulfillment_identity_guard ON entities;
        DROP FUNCTION IF EXISTS prismedia_guard_fulfillment_identity();
        DROP TRIGGER IF EXISTS monitor_fulfillment_guard ON monitors;
        DROP TRIGGER IF EXISTS acquisition_fulfillment_guard ON acquisitions;
        DROP TRIGGER IF EXISTS fulfillment_reservation_guard ON fulfillment_reservations;
        DROP FUNCTION IF EXISTS prismedia_guard_native_fulfillment();
        DROP FUNCTION IF EXISTS prismedia_guard_external_fulfillment();
        DROP FUNCTION IF EXISTS prismedia_check_external_fulfillment(fulfillment_reservations);
        DROP FUNCTION IF EXISTS prismedia_fulfillment_matches_acquisition(fulfillment_reservations, acquisitions);
        DROP FUNCTION IF EXISTS prismedia_fulfillment_matches(fulfillment_reservations, uuid, text, text, text, text);
        DROP FUNCTION IF EXISTS prismedia_fulfillment_overlap(uuid, uuid);
        DROP FUNCTION IF EXISTS prismedia_fulfillment_equivalent(uuid, uuid, integer);
        """;
}
