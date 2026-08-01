using Microsoft.EntityFrameworkCore.Migrations;
using Prismedia.Application.Jobs;
using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Processing;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations;

/// <summary>
/// Releases direct Movie and VideoEpisode persistence while conservatively collapsing the legacy
/// Movie-to-Video wrapper. Every durable reference is either retargeted by an exact identifier or
/// rejected before any destructive work occurs.
/// </summary>
public partial class MigrateDirectPlayableEntities : Migration {
    internal const string LegacyPerformerRoleCode = "performer";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(BuildUpSql());

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        """
        DO $prismedia$
        BEGIN
            RAISE EXCEPTION USING
                ERRCODE = 'P0001',
                MESSAGE = 'MigrateDirectPlayableEntities is irreversible: legacy Movie child identities cannot be reconstructed safely.';
        END
        $prismedia$;
        """);

    private static string BuildUpSql() =>
        """
        SELECT pg_advisory_xact_lock(hashtextextended('__ADVISORY_LOCK__', 0));

        LOCK TABLE
            entities,
            library_roots,
            entity_library_roots,
            entity_files,
            entity_sources,
            entity_relationship_links,
            collection_item_details,
            collection_details,
            user_entity_states,
            acquisitions,
            acquisition_history,
            acquisition_import_hints,
            monitors,
            entity_subtitles,
            identify_queue_items,
            job_graphs,
            job_graph_signals,
            job_runs,
            job_resource_states,
            job_resource_leases
        IN SHARE ROW EXCLUSIVE MODE;

        CREATE TEMP TABLE prismedia_playable_map (
            old_id uuid PRIMARY KEY,
            new_id uuid NOT NULL,
            old_kind text NOT NULL,
            new_kind text NOT NULL,
            mode text NOT NULL
        ) ON COMMIT DROP;

        INSERT INTO pg_temp.prismedia_playable_map (old_id, new_id, old_kind, new_kind, mode)
        SELECT child.id, movie.id, '__VIDEO_KIND__', '__MOVIE_KIND__', 'collapse'
        FROM entities AS child
        INNER JOIN entities AS movie ON movie.id = child.parent_entity_id
        WHERE child.kind_code = '__VIDEO_KIND__'
          AND movie.kind_code = '__MOVIE_KIND__';

        INSERT INTO pg_temp.prismedia_playable_map (old_id, new_id, old_kind, new_kind, mode)
        SELECT child.id, child.id, '__VIDEO_KIND__', '__VIDEO_EPISODE_KIND__', 'episode'
        FROM entities AS child
        INNER JOIN entities AS parent ON parent.id = child.parent_entity_id
        WHERE child.kind_code = '__VIDEO_KIND__'
          AND parent.kind_code IN ('__VIDEO_SERIES_KIND__', '__VIDEO_SEASON_KIND__');

        CREATE TEMP TABLE prismedia_manifest_presence (was_prepared boolean NOT NULL) ON COMMIT DROP;
        INSERT INTO pg_temp.prismedia_manifest_presence
        VALUES (to_regclass('pg_temp.__MANIFEST_TABLE__') IS NOT NULL);

        CREATE TEMP TABLE IF NOT EXISTS __MANIFEST_TABLE__ (
            subject text NOT NULL,
            row_id uuid NOT NULL,
            column_name text NOT NULL,
            old_entity_id uuid NOT NULL,
            new_entity_id uuid NOT NULL,
            old_value text NOT NULL,
            new_value text NOT NULL,
            classification text NULL,
            PRIMARY KEY (subject, row_id, column_name)
        ) ON COMMIT PRESERVE ROWS;

        DO $prismedia$
        DECLARE
            collision_table text;
            has_collision boolean;
        BEGIN
            IF EXISTS (
                SELECT 1
                FROM entities AS child
                INNER JOIN entities AS parent ON parent.id = child.parent_entity_id
                WHERE child.kind_code = '__VIDEO_KIND__'
                  AND parent.kind_code NOT IN (
                      '__MOVIE_KIND__', '__VIDEO_SERIES_KIND__', '__VIDEO_SEASON_KIND__')
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found a legacy Video with an unsupported parent kind';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM entities AS movie
                INNER JOIN entities AS child ON child.parent_entity_id = movie.id
                WHERE movie.kind_code = '__MOVIE_KIND__'
                GROUP BY movie.id
                HAVING count(*) FILTER (WHERE child.kind_code = '__VIDEO_KIND__') > 1
                    OR count(*) FILTER (WHERE child.kind_code <> '__VIDEO_KIND__') > 0
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found an ambiguous Movie child graph';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM entities AS descendant
                INNER JOIN pg_temp.prismedia_playable_map AS map
                    ON map.old_id = descendant.parent_entity_id
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found a mapped legacy Video that still owns child Entities';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM entities AS entity
                INNER JOIN pg_temp.prismedia_playable_map AS map
                    ON map.old_id = entity.id
                    OR (map.mode = 'collapse' AND map.new_id = entity.id)
                WHERE entity.lifecycle_claim_id IS NOT NULL
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found an active Entity lifecycle claim';
            END IF;

            IF EXISTS (SELECT 1 FROM pg_temp.prismedia_playable_map WHERE mode = 'collapse')
               AND NOT (SELECT was_prepared FROM pg_temp.prismedia_manifest_presence) THEN
                RAISE EXCEPTION 'Direct-playable migration requires its filesystem preparation manifest';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM pg_temp.prismedia_playable_map AS map
                LEFT JOIN pg_temp.__MANIFEST_TABLE__ AS manifest
                    ON manifest.subject = '__MOVIE_MAPPING_SUBJECT__'
                   AND manifest.row_id = map.old_id
                   AND manifest.old_entity_id = map.old_id
                   AND manifest.new_entity_id = map.new_id
                WHERE map.mode = 'collapse' AND manifest.row_id IS NULL
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration filesystem manifest does not match the Movie collapse set';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM pg_temp.prismedia_playable_map AS map
                LEFT JOIN LATERAL (
                    SELECT count(*) AS source_count,
                           (array_agg(file.id ORDER BY file.id))[1] AS file_id,
                           min(file.path) AS path
                    FROM entity_files AS file
                    WHERE file.entity_id = map.old_id AND file.role = '__SOURCE_ROLE__'
                ) AS source ON true
                LEFT JOIN pg_temp.__MANIFEST_TABLE__ AS manifest
                    ON manifest.subject = '__MOVIE_PAYLOAD_SUBJECT__'
                   AND manifest.row_id = source.file_id
                   AND manifest.old_entity_id = map.old_id
                   AND manifest.new_entity_id = map.new_id
                   AND manifest.old_value = source.path
                   AND manifest.classification = '__FILE_CLASS__'
                WHERE map.mode = 'collapse'
                  AND (source.source_count <> 1 OR manifest.row_id IS NULL)
            ) THEN
                RAISE EXCEPTION 'Every collapsing Movie Video must own exactly one filesystem-verified source file';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM entity_files AS file
                INNER JOIN entities AS entity ON entity.id = file.entity_id
                LEFT JOIN pg_temp.__MANIFEST_TABLE__ AS manifest
                    ON manifest.subject = '__SOURCE_CLASSIFICATION_SUBJECT__'
                   AND manifest.row_id = file.id
                   AND manifest.old_entity_id = file.entity_id
                   AND manifest.old_value = file.path
                WHERE file.role = '__SOURCE_ROLE__'
                  AND entity.kind_code IN (
                      '__MOVIE_KIND__', '__VIDEO_SERIES_KIND__', '__VIDEO_SEASON_KIND__',
                      '__GALLERY_KIND__', '__AUDIO_LIBRARY_KIND__', '__MUSIC_ARTIST_KIND__',
                      '__BOOK_AUTHOR_KIND__', '__BOOK_KIND__', '__BOOK_VOLUME_KIND__')
                  AND (manifest.row_id IS NULL OR manifest.classification NOT IN ('__FILE_CLASS__', '__FOLDER_CLASS__'))
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration cannot classify every legacy structural source path';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM entity_files AS file
                INNER JOIN entities AS entity ON entity.id = file.entity_id
                INNER JOIN pg_temp.__MANIFEST_TABLE__ AS manifest
                    ON manifest.subject = '__SOURCE_CLASSIFICATION_SUBJECT__' AND manifest.row_id = file.id
                WHERE file.role = '__SOURCE_ROLE__'
                  AND entity.kind_code IN (
                      '__VIDEO_SERIES_KIND__', '__VIDEO_SEASON_KIND__', '__GALLERY_KIND__',
                      '__AUDIO_LIBRARY_KIND__', '__MUSIC_ARTIST_KIND__', '__BOOK_AUTHOR_KIND__',
                      '__BOOK_VOLUME_KIND__')
                  AND manifest.classification <> '__FOLDER_CLASS__'
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found a structural Entity whose source is not a folder';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM pg_temp.prismedia_playable_map AS map
                INNER JOIN entity_files AS file
                    ON file.entity_id = map.new_id AND file.role = '__SOURCE_ROLE__'
                INNER JOIN pg_temp.__MANIFEST_TABLE__ AS manifest
                    ON manifest.subject = '__SOURCE_CLASSIFICATION_SUBJECT__' AND manifest.row_id = file.id
                WHERE map.mode = 'collapse' AND manifest.classification <> '__FOLDER_CLASS__'
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found a legacy Movie whose structural source is not a folder';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM entity_files AS file
                INNER JOIN pg_temp.__MANIFEST_TABLE__ AS manifest
                    ON manifest.subject = '__SOURCE_CLASSIFICATION_SUBJECT__'
                   AND manifest.row_id = file.id
                   AND manifest.classification = '__FOLDER_CLASS__'
                WHERE EXISTS (SELECT 1 FROM entity_file_fingerprints WHERE entity_file_id = file.id)
                   OR EXISTS (SELECT 1 FROM media_sources WHERE entity_file_id = file.id)
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration cannot remove a structural folder row referenced as playable media';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM entity_sources AS source
                INNER JOIN entity_files AS file ON file.entity_id = source.entity_id
                INNER JOIN pg_temp.__MANIFEST_TABLE__ AS manifest
                    ON manifest.subject = '__SOURCE_CLASSIFICATION_SUBJECT__'
                   AND manifest.row_id = file.id
                   AND manifest.classification = '__FOLDER_CLASS__'
                WHERE source.code = '__FOLDER_SOURCE_CODE__' AND source.value <> file.path
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found conflicting folder provenance';
            END IF;

            IF EXISTS (
                SELECT 1 FROM acquisitions AS acquisition
                INNER JOIN pg_temp.prismedia_playable_map AS map ON map.old_id = acquisition.entity_id
                WHERE map.mode = 'collapse'
                  AND acquisition.status IN (
                      '__ACQUISITION_SEARCHING__', '__ACQUISITION_QUEUED__',
                      '__ACQUISITION_DOWNLOADING__', '__ACQUISITION_WAITING_CLIENT__',
                      '__ACQUISITION_DOWNLOADED__', '__ACQUISITION_IMPORTING__',
                      '__ACQUISITION_STOPPING__')
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found active acquisition work for a collapsing Video';
            END IF;

            IF EXISTS (
                SELECT 1 FROM acquisitions AS acquisition
                INNER JOIN pg_temp.prismedia_playable_map AS map ON map.old_id = acquisition.entity_id
                WHERE map.mode = 'collapse'
                  AND acquisition.tv_import_checkpoint_json IS NOT NULL
                  AND (jsonb_typeof(acquisition.tv_import_checkpoint_json) <> 'object'
                       OR acquisition.tv_import_checkpoint_json ->> 'Kind' IS DISTINCT FROM '__MOVIE_KIND__')
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found a retained import checkpoint whose protocol would change during Movie collapse';
            END IF;

            IF EXISTS (
                SELECT 1 FROM monitors AS monitor
                INNER JOIN pg_temp.prismedia_playable_map AS map ON map.old_id = monitor.entity_id
                WHERE map.mode = 'collapse'
                  AND monitor.status IN ('__MONITOR_DELETING__', '__MONITOR_STOPPING__')
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found destructive monitor work for a collapsing Video';
            END IF;

            FOREACH collision_table IN ARRAY ARRAY[
                'entity_classifications', 'entity_descriptions', 'entity_library_roots',
                'entity_lifetimes', 'entity_provider_identities', 'entity_subtitle_states',
                'entity_technical'
            ] LOOP
                EXECUTE format(
                    'SELECT EXISTS (SELECT 1 FROM %I AS old_row INNER JOIN pg_temp.prismedia_playable_map AS map ON map.old_id = old_row.entity_id INNER JOIN %I AS new_row ON new_row.entity_id = map.new_id WHERE map.mode = ''collapse'')',
                    collision_table,
                    collision_table)
                INTO has_collision;
                IF has_collision THEN
                    RAISE EXCEPTION 'Direct-playable migration found conflicting rows in %', collision_table;
                END IF;
            END LOOP;

            IF EXISTS (
                SELECT 1
                FROM pg_temp.prismedia_playable_map AS map
                INNER JOIN monitors AS old_row ON old_row.entity_id = map.old_id
                INNER JOIN monitors AS new_row ON new_row.entity_id = map.new_id
                    AND new_row.book_rendition IS NOT DISTINCT FROM old_row.book_rendition
                WHERE map.mode = 'collapse'
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found conflicting monitor identities';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM pg_temp.prismedia_playable_map AS map
                INNER JOIN entities AS entity ON entity.id = map.old_id
                WHERE map.mode = 'collapse' AND EXISTS (
                    SELECT 1 FROM audio_track_details WHERE entity_id = entity.id UNION ALL
                    SELECT 1 FROM book_chapter_details WHERE entity_id = entity.id UNION ALL
                    SELECT 1 FROM book_details WHERE entity_id = entity.id UNION ALL
                    SELECT 1 FROM collection_details WHERE entity_id = entity.id UNION ALL
                    SELECT 1 FROM gallery_details WHERE entity_id = entity.id UNION ALL
                    SELECT 1 FROM person_details WHERE entity_id = entity.id UNION ALL
                    SELECT 1 FROM tag_details WHERE entity_id = entity.id UNION ALL
                    SELECT 1 FROM video_series_details WHERE entity_id = entity.id
                )
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found kind-specific detail data on a legacy Video child';
            END IF;
        END
        $prismedia$;

        DO $prismedia$
        BEGIN
            IF (SELECT count(*) FROM pg_temp.__MANIFEST_TABLE__ WHERE subject = '__LIBRARY_ROOT_SNAPSHOT_SUBJECT__')
               <> (SELECT count(*) FROM library_roots)
               OR EXISTS (
                    SELECT 1
                    FROM pg_temp.__MANIFEST_TABLE__ AS manifest
                    LEFT JOIN library_roots AS root ON root.id = manifest.row_id
                    WHERE manifest.subject = '__LIBRARY_ROOT_SNAPSHOT_SUBJECT__'
                      AND (
                          manifest.column_name <> 'path'
                          OR manifest.old_entity_id <> manifest.row_id
                          OR manifest.new_entity_id <> manifest.row_id
                          OR root.id IS NULL
                          OR root.path IS DISTINCT FROM manifest.old_value
                          OR manifest.new_value IS DISTINCT FROM manifest.old_value)
               )
               OR EXISTS (
                    SELECT 1
                    FROM library_roots AS root
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM pg_temp.__MANIFEST_TABLE__ AS manifest
                        WHERE manifest.subject = '__LIBRARY_ROOT_SNAPSHOT_SUBJECT__'
                          AND manifest.row_id = root.id)
               ) THEN
                RAISE EXCEPTION 'Direct-playable migration library-root snapshot no longer matches configured roots';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM pg_temp.__MANIFEST_TABLE__ AS manifest
                LEFT JOIN entities AS entity ON entity.id = manifest.row_id
                LEFT JOIN entity_library_roots AS direct_root ON direct_root.entity_id = entity.id
                WHERE manifest.subject = '__LIBRARY_ROOT_BACKFILL_SUBJECT__'
                      AND (
                          manifest.column_name <> 'library_root_id'
                          OR manifest.old_entity_id <> manifest.row_id
                          OR manifest.new_entity_id <> manifest.row_id
                          OR entity.id IS NULL
                          OR entity.kind_code NOT IN ('__IMAGE_KIND__', '__AUDIO_TRACK_KIND__')
                      OR direct_root.library_root_id IS NOT NULL
                      OR (SELECT count(*)
                          FROM entity_files AS source
                          WHERE source.entity_id = manifest.row_id
                            AND source.role = '__SOURCE_ROLE__') <> 1
                      OR NOT EXISTS (
                          SELECT 1
                          FROM entity_files AS source
                          WHERE source.entity_id = manifest.row_id
                            AND source.role = '__SOURCE_ROLE__'
                            AND source.path = manifest.old_value)
                      OR NOT EXISTS (
                          SELECT 1
                          FROM library_roots AS root
                          WHERE root.id = manifest.new_value::uuid)
                  )
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration library-root manifest no longer matches source-backed media';
            END IF;

            IF EXISTS (
                SELECT entity.id
                FROM entities AS entity
                INNER JOIN entity_files AS source
                    ON source.entity_id = entity.id AND source.role = '__SOURCE_ROLE__'
                LEFT JOIN entity_library_roots AS direct_root ON direct_root.entity_id = entity.id
                LEFT JOIN pg_temp.__MANIFEST_TABLE__ AS manifest
                    ON manifest.subject = '__LIBRARY_ROOT_BACKFILL_SUBJECT__'
                   AND manifest.row_id = entity.id
                WHERE entity.kind_code IN ('__IMAGE_KIND__', '__AUDIO_TRACK_KIND__')
                  AND direct_root.library_root_id IS NULL
                GROUP BY entity.id, manifest.row_id
                HAVING count(source.id) <> 1 OR manifest.row_id IS NULL
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration requires a complete library-root backfill manifest';
            END IF;
        END
        $prismedia$;

        INSERT INTO entity_library_roots (entity_id, library_root_id)
        SELECT manifest.row_id, manifest.new_value::uuid
        FROM pg_temp.__MANIFEST_TABLE__ AS manifest
        WHERE manifest.subject = '__LIBRARY_ROOT_BACKFILL_SUBJECT__'
        ON CONFLICT (entity_id) DO UPDATE
        SET library_root_id = EXCLUDED.library_root_id
        WHERE entity_library_roots.library_root_id IS NULL;

        INSERT INTO entity_kinds (code, display_name, category, storage_shape)
        VALUES ('__VIDEO_EPISODE_KIND__', 'Video Episode', '__MEDIA_CATEGORY__', '__FILE_SHAPE__')
        ON CONFLICT (code) DO UPDATE
        SET display_name = EXCLUDED.display_name,
            category = EXCLUDED.category,
            storage_shape = EXCLUDED.storage_shape;

        UPDATE entity_kinds
        SET storage_shape = '__FILE_SHAPE__'
        WHERE code = '__MOVIE_KIND__';

        UPDATE entity_relationship_links
        SET metadata_json = CASE
            WHEN jsonb_typeof(metadata_json) <> 'object' THEN metadata_json
            ELSE
                (CASE
                    WHEN metadata_json ->> 'role' = '__LEGACY_PERFORMER_ROLE__'
                        THEN jsonb_set(metadata_json, '{role}', to_jsonb('__ACTOR_ROLE__'::text), false)
                    ELSE metadata_json
                 END)
                || CASE
                    WHEN jsonb_typeof(metadata_json -> 'roles') = 'array' THEN
                        jsonb_build_object(
                            'roles',
                            COALESCE((
                                SELECT jsonb_agg(value ORDER BY first_ordinal)
                                FROM (
                                    SELECT value, min(ordinality) AS first_ordinal
                                    FROM (
                                        SELECT CASE
                                                   WHEN value = to_jsonb('__LEGACY_PERFORMER_ROLE__'::text)
                                                       THEN to_jsonb('__ACTOR_ROLE__'::text)
                                                   ELSE value
                                               END AS value,
                                               ordinality
                                        FROM jsonb_array_elements(metadata_json -> 'roles')
                                             WITH ORDINALITY AS item(value, ordinality)
                                    ) AS normalized
                                    GROUP BY value
                                ) AS deduplicated
                            ), '[]'::jsonb))
                    ELSE '{}'::jsonb
                END
        END
        WHERE jsonb_typeof(metadata_json) = 'object'
          AND relationship_code IN ('__CAST_RELATIONSHIP__', '__CREDITS_RELATIONSHIP__')
          AND (metadata_json ->> 'role' = '__LEGACY_PERFORMER_ROLE__'
               OR metadata_json -> 'roles' @> jsonb_build_array('__LEGACY_PERFORMER_ROLE__'::text));

        INSERT INTO entity_sources (entity_id, code, value, updated_at)
        SELECT file.entity_id, '__FOLDER_SOURCE_CODE__', file.path, file.updated_at
        FROM entity_files AS file
        INNER JOIN pg_temp.__MANIFEST_TABLE__ AS manifest
            ON manifest.subject = '__SOURCE_CLASSIFICATION_SUBJECT__'
           AND manifest.row_id = file.id
           AND manifest.classification = '__FOLDER_CLASS__'
        ON CONFLICT (entity_id, code) DO UPDATE
        SET value = EXCLUDED.value,
            updated_at = GREATEST(entity_sources.updated_at, EXCLUDED.updated_at);

        DELETE FROM entity_files AS file
        USING pg_temp.__MANIFEST_TABLE__ AS manifest
        WHERE manifest.subject = '__SOURCE_CLASSIFICATION_SUBJECT__'
          AND manifest.row_id = file.id
          AND manifest.classification = '__FOLDER_CLASS__';

        DO $prismedia$
        BEGIN
            IF EXISTS (
                SELECT 1
                FROM pg_temp.__MANIFEST_TABLE__ AS manifest
                LEFT JOIN entity_subtitles AS subtitle ON subtitle.id = manifest.row_id
                WHERE manifest.subject = '__SUBTITLE_SUBJECT__'
                  AND (subtitle.id IS NULL
                       OR CASE manifest.column_name
                              WHEN 'storage_path' THEN subtitle.storage_path
                              WHEN 'source_path' THEN subtitle.source_path
                              ELSE NULL
                          END IS DISTINCT FROM manifest.old_value)
            ) OR EXISTS (
                SELECT 1
                FROM pg_temp.__MANIFEST_TABLE__ AS manifest
                LEFT JOIN entity_files AS file ON file.id = manifest.row_id
                WHERE manifest.subject = '__ENTITY_FILE_SUBJECT__'
                  AND (manifest.column_name <> 'path' OR file.path IS DISTINCT FROM manifest.old_value)
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration filesystem manifest no longer matches database paths';
            END IF;
        END
        $prismedia$;

        UPDATE entity_subtitles AS subtitle
        SET storage_path = manifest.new_value
        FROM pg_temp.__MANIFEST_TABLE__ AS manifest
        WHERE manifest.subject = '__SUBTITLE_SUBJECT__'
          AND manifest.column_name = 'storage_path'
          AND subtitle.id = manifest.row_id;

        UPDATE entity_subtitles AS subtitle
        SET source_path = manifest.new_value
        FROM pg_temp.__MANIFEST_TABLE__ AS manifest
        WHERE manifest.subject = '__SUBTITLE_SUBJECT__'
          AND manifest.column_name = 'source_path'
          AND subtitle.id = manifest.row_id;

        UPDATE entity_files AS file
        SET path = manifest.new_value
        FROM pg_temp.__MANIFEST_TABLE__ AS manifest
        WHERE manifest.subject = '__ENTITY_FILE_SUBJECT__'
          AND manifest.column_name = 'path'
          AND file.id = manifest.row_id;

        DELETE FROM trickplay_infos AS detail
        USING pg_temp.prismedia_playable_map AS map
        WHERE map.mode = 'collapse' AND detail.entity_id = map.old_id;

        DELETE FROM entity_files AS file
        USING pg_temp.prismedia_playable_map AS map
        WHERE map.mode = 'collapse'
          AND file.entity_id = map.old_id
          AND file.source = '__SCAN_SOURCE__'
          AND file.role IN (
              '__THUMBNAIL_ROLE__', '__GRID_THUMBNAIL_ROLE__', '__GRID_THUMBNAIL_2X_ROLE__',
              '__PREVIEW_ROLE__', '__SPRITE_ROLE__', '__TRICKPLAY_ROLE__', '__HLS_ROLE__');

        DO $prismedia$
        BEGIN
            IF EXISTS (
                SELECT 1
                FROM entity_files AS old_row
                INNER JOIN pg_temp.prismedia_playable_map AS map ON map.old_id = old_row.entity_id
                INNER JOIN entity_files AS new_row
                    ON new_row.entity_id = map.new_id AND new_row.role = old_row.role
                WHERE map.mode = 'collapse'
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found conflicting Entity file roles';
            END IF;

            IF EXISTS (
                SELECT 1 FROM entity_file_fingerprints AS old_row
                INNER JOIN pg_temp.prismedia_playable_map AS map ON map.old_id = old_row.entity_id
                INNER JOIN entity_file_fingerprints AS new_row
                    ON new_row.entity_id = map.new_id AND new_row.algorithm = old_row.algorithm
                WHERE map.mode = 'collapse'
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found conflicting file fingerprints';
            END IF;

            IF EXISTS (
                SELECT 1 FROM media_sources AS old_row
                INNER JOIN pg_temp.prismedia_playable_map AS map ON map.old_id = old_row.entity_id
                INNER JOIN media_sources AS new_row
                    ON new_row.entity_id = map.new_id AND new_row.path = old_row.path
                WHERE map.mode = 'collapse'
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found conflicting media sources';
            END IF;

            IF EXISTS (
                SELECT 1 FROM entity_dates AS old_row
                INNER JOIN pg_temp.prismedia_playable_map AS map ON map.old_id = old_row.entity_id
                INNER JOIN entity_dates AS new_row ON new_row.entity_id = map.new_id AND new_row.code = old_row.code
                WHERE map.mode = 'collapse'
            ) OR EXISTS (
                SELECT 1 FROM entity_external_ids AS old_row
                INNER JOIN pg_temp.prismedia_playable_map AS map ON map.old_id = old_row.entity_id
                INNER JOIN entity_external_ids AS new_row ON new_row.entity_id = map.new_id AND new_row.provider = old_row.provider
                WHERE map.mode = 'collapse'
            ) OR EXISTS (
                SELECT 1 FROM entity_positions AS old_row
                INNER JOIN pg_temp.prismedia_playable_map AS map ON map.old_id = old_row.entity_id
                INNER JOIN entity_positions AS new_row ON new_row.entity_id = map.new_id AND new_row.code = old_row.code
                WHERE map.mode = 'collapse'
            ) OR EXISTS (
                SELECT 1 FROM entity_sources AS old_row
                INNER JOIN pg_temp.prismedia_playable_map AS map ON map.old_id = old_row.entity_id
                INNER JOIN entity_sources AS new_row ON new_row.entity_id = map.new_id AND new_row.code = old_row.code
                WHERE map.mode = 'collapse'
            ) OR EXISTS (
                SELECT 1 FROM entity_stats AS old_row
                INNER JOIN pg_temp.prismedia_playable_map AS map ON map.old_id = old_row.entity_id
                INNER JOIN entity_stats AS new_row ON new_row.entity_id = map.new_id AND new_row.code = old_row.code
                WHERE map.mode = 'collapse'
            ) OR EXISTS (
                SELECT 1 FROM entity_subtitles AS old_row
                INNER JOIN pg_temp.prismedia_playable_map AS map ON map.old_id = old_row.entity_id
                INNER JOIN entity_subtitles AS new_row
                    ON new_row.entity_id = map.new_id AND new_row.source = old_row.source AND new_row.source_key = old_row.source_key
                WHERE map.mode = 'collapse'
            ) OR EXISTS (
                SELECT 1 FROM entity_urls AS old_row
                INNER JOIN pg_temp.prismedia_playable_map AS map ON map.old_id = old_row.entity_id
                INNER JOIN entity_urls AS new_row ON new_row.entity_id = map.new_id AND new_row.url = old_row.url
                WHERE map.mode = 'collapse'
            ) OR EXISTS (
                SELECT 1 FROM fingerprint_submissions AS old_row
                INNER JOIN pg_temp.prismedia_playable_map AS map ON map.old_id = old_row.entity_id
                INNER JOIN fingerprint_submissions AS new_row
                    ON new_row.entity_id = map.new_id
                   AND new_row.algorithm = old_row.algorithm
                   AND new_row.hash = old_row.hash
                WHERE map.mode = 'collapse'
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found conflicting keyed capability rows';
            END IF;
        END
        $prismedia$;

        CREATE TEMP TABLE prismedia_retired_identify_graphs (id uuid PRIMARY KEY) ON COMMIT DROP;
        INSERT INTO pg_temp.prismedia_retired_identify_graphs (id)
        SELECT DISTINCT item.job_graph_id
        FROM identify_queue_items AS item
        INNER JOIN pg_temp.prismedia_playable_map AS map ON map.old_id = item.entity_id
        WHERE item.state NOT IN ('__IDENTIFY_DONE__', '__IDENTIFY_DELETED__')
          AND item.job_graph_id IS NOT NULL
        UNION
        SELECT DISTINCT run.graph_id
        FROM identify_queue_items AS item
        INNER JOIN pg_temp.prismedia_playable_map AS map ON map.old_id = item.entity_id
        INNER JOIN job_runs AS run
            ON run.id = item.search_job_id OR run.id = item.cascade_job_id
        WHERE item.state NOT IN ('__IDENTIFY_DONE__', '__IDENTIFY_DELETED__')
          AND run.graph_id IS NOT NULL;

        CREATE TEMP TABLE prismedia_retired_identify_runs (id uuid PRIMARY KEY) ON COMMIT DROP;
        INSERT INTO pg_temp.prismedia_retired_identify_runs (id)
        SELECT run.id
        FROM job_runs AS run
        INNER JOIN pg_temp.prismedia_retired_identify_graphs AS graph ON graph.id = run.graph_id
        WHERE run.status IN ('__JOB_QUEUED__', '__JOB_RUNNING__')
        UNION
        SELECT run.id
        FROM identify_queue_items AS item
        INNER JOIN pg_temp.prismedia_playable_map AS map ON map.old_id = item.entity_id
        INNER JOIN job_runs AS run
            ON run.id = item.search_job_id OR run.id = item.cascade_job_id
        WHERE item.state NOT IN ('__IDENTIFY_DONE__', '__IDENTIFY_DELETED__')
          AND run.status IN ('__JOB_QUEUED__', '__JOB_RUNNING__');

        UPDATE job_runs AS run
        SET status = '__JOB_CANCELLED__',
            message = 'Cancelled with graph.',
            locked_at = NULL,
            locked_by = NULL,
            finished_at = now()
        FROM pg_temp.prismedia_retired_identify_runs AS retired
        WHERE retired.id = run.id;

        UPDATE job_graph_signals AS signal
        SET cancelled_at = now()
        FROM pg_temp.prismedia_retired_identify_graphs AS retired
        WHERE retired.id = signal.graph_id
          AND signal.resolved_at IS NULL
          AND signal.cancelled_at IS NULL;

        DELETE FROM job_resource_leases AS lease
        USING pg_temp.prismedia_retired_identify_runs AS retired
        WHERE retired.id = lease.job_run_id;

        UPDATE job_graphs AS graph
        SET cancellation_requested = TRUE,
            status = '__GRAPH_CANCELLED__',
            updated_at = now(),
            finished_at = now()
        FROM pg_temp.prismedia_retired_identify_graphs AS retired
        WHERE retired.id = graph.id
          AND graph.status IN ('__GRAPH_QUEUED__', '__GRAPH_RUNNING__', '__GRAPH_WAITING__');

        UPDATE identify_queue_items AS item
        SET state = '__IDENTIFY_DELETED__',
            error = NULL,
            search_job_id = NULL,
            cascade_job_id = NULL,
            updated_at = now(),
            completed_at = now()
        FROM pg_temp.prismedia_playable_map AS map
        WHERE map.old_id = item.entity_id
          AND item.state NOT IN ('__IDENTIFY_DONE__', '__IDENTIFY_DELETED__');

        DELETE FROM identify_queue_items AS child
        USING pg_temp.prismedia_playable_map AS map
        WHERE map.mode = 'collapse'
          AND child.entity_id = map.old_id
          AND EXISTS (
              SELECT 1
              FROM identify_queue_items AS survivor
              WHERE survivor.entity_id = map.new_id);

        UPDATE identify_queue_items AS item
        SET entity_id = map.new_id
        FROM pg_temp.prismedia_playable_map AS map
        WHERE map.mode = 'collapse' AND item.entity_id = map.old_id;

        UPDATE entity_files AS row SET entity_id = map.new_id
        FROM pg_temp.prismedia_playable_map AS map
        WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE entity_file_fingerprints AS row SET entity_id = map.new_id
        FROM pg_temp.prismedia_playable_map AS map
        WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE media_sources AS row SET entity_id = map.new_id
        FROM pg_temp.prismedia_playable_map AS map
        WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE media_streams AS row SET entity_id = map.new_id
        FROM pg_temp.prismedia_playable_map AS map
        WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;

        UPDATE entity_dates AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE entity_external_ids AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE entity_positions AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE entity_sources AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE entity_stats AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE entity_subtitles AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE entity_urls AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE fingerprint_submissions AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;

        UPDATE entity_classifications AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE entity_descriptions AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE entity_library_roots AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE entity_lifetimes AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE entity_provider_identities AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE entity_subtitle_states AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE entity_technical AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;

        UPDATE user_entity_states AS row
        SET progress_current_entity_id = map.new_id
        FROM pg_temp.prismedia_playable_map AS map
        WHERE map.mode = 'collapse' AND row.progress_current_entity_id = map.old_id;

        UPDATE user_entity_states AS parent_state
        SET is_favorite = parent_state.is_favorite OR child_state.is_favorite,
            rating_value = COALESCE(parent_state.rating_value, child_state.rating_value),
            play_count = GREATEST(parent_state.play_count, child_state.play_count),
            skip_count = GREATEST(parent_state.skip_count, child_state.skip_count),
            play_duration_seconds = GREATEST(
                parent_state.play_duration_seconds,
                child_state.play_duration_seconds),
            resume_seconds = child_state.resume_seconds,
            last_played_at = GREATEST(parent_state.last_played_at, child_state.last_played_at),
            completed_at = GREATEST(parent_state.completed_at, child_state.completed_at),
            progress_current_entity_id = child_state.progress_current_entity_id,
            progress_unit = child_state.progress_unit,
            progress_index = child_state.progress_index,
            progress_total = child_state.progress_total,
            progress_mode = child_state.progress_mode,
            progress_location = child_state.progress_location,
            progress_completed_at = GREATEST(
                parent_state.progress_completed_at,
                child_state.progress_completed_at),
            updated_at = GREATEST(parent_state.updated_at, child_state.updated_at)
        FROM user_entity_states AS child_state
        INNER JOIN pg_temp.prismedia_playable_map AS map
            ON map.old_id = child_state.entity_id AND map.mode = 'collapse'
        WHERE parent_state.entity_id = map.new_id
          AND parent_state.user_id = child_state.user_id;

        DELETE FROM user_entity_states AS child_state
        USING pg_temp.prismedia_playable_map AS map, user_entity_states AS parent_state
        WHERE map.mode = 'collapse'
          AND child_state.entity_id = map.old_id
          AND parent_state.entity_id = map.new_id
          AND parent_state.user_id = child_state.user_id;

        UPDATE user_entity_states AS row
        SET entity_id = map.new_id
        FROM pg_temp.prismedia_playable_map AS map
        WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;

        UPDATE entity_activity_events AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE entity_markers AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE entity_playback_events AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;
        UPDATE identify_results AS row SET entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.entity_id = map.old_id;

        CREATE TEMP TABLE prismedia_relationship_projection ON COMMIT DROP AS
        SELECT
            COALESCE(owner_map.new_id, relation.entity_id) AS entity_id,
            relation.relationship_code,
            COALESCE(target_map.new_id, relation.target_entity_id) AS target_entity_id,
            relation.label,
            COALESCE(target_map.new_kind, relation.target_kind_code) AS target_kind_code,
            relation.sort_order,
            relation.metadata_json,
            relation.created_at
        FROM entity_relationship_links AS relation
        LEFT JOIN pg_temp.prismedia_playable_map AS owner_map ON owner_map.old_id = relation.entity_id
        LEFT JOIN pg_temp.prismedia_playable_map AS target_map ON target_map.old_id = relation.target_entity_id
        WHERE owner_map.old_id IS NOT NULL OR target_map.old_id IS NOT NULL;

        DO $prismedia$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM pg_temp.prismedia_relationship_projection
                WHERE entity_id = target_entity_id
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration would create a self relationship';
            END IF;

            IF EXISTS (
                SELECT entity_id, relationship_code, target_entity_id
                FROM (
                    SELECT * FROM pg_temp.prismedia_relationship_projection
                    UNION ALL
                    SELECT relation.* FROM entity_relationship_links AS relation
                    WHERE NOT EXISTS (
                        SELECT 1 FROM pg_temp.prismedia_playable_map AS map
                        WHERE map.old_id = relation.entity_id OR map.old_id = relation.target_entity_id)
                ) AS projected
                GROUP BY entity_id, relationship_code, target_entity_id
                HAVING count(DISTINCT jsonb_build_object(
                    'label', label,
                    'targetKind', target_kind_code,
                    'sortOrder', sort_order,
                    'metadata', metadata_json,
                    'createdAt', created_at)) > 1
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found conflicting relationship projections';
            END IF;
        END
        $prismedia$;

        DELETE FROM entity_relationship_links AS relation
        WHERE EXISTS (
            SELECT 1 FROM pg_temp.prismedia_playable_map AS map
            WHERE map.old_id = relation.entity_id OR map.old_id = relation.target_entity_id);

        INSERT INTO entity_relationship_links (
            entity_id, relationship_code, target_entity_id, label, target_kind_code,
            sort_order, metadata_json, created_at)
        SELECT DISTINCT ON (entity_id, relationship_code, target_entity_id)
            entity_id, relationship_code, target_entity_id, label, target_kind_code,
            sort_order, metadata_json, created_at
        FROM pg_temp.prismedia_relationship_projection
        ORDER BY entity_id, relationship_code, target_entity_id, created_at;

        CREATE TEMP TABLE prismedia_collection_item_projection ON COMMIT DROP AS
        SELECT
            item.id,
            COALESCE(collection_map.new_id, item.collection_entity_id) AS collection_entity_id,
            COALESCE(item_map.new_id, item.item_entity_id) AS item_entity_id,
            item.source,
            item.sort_order,
            item.added_at
        FROM collection_item_details AS item
        LEFT JOIN pg_temp.prismedia_playable_map AS collection_map ON collection_map.old_id = item.collection_entity_id
        LEFT JOIN pg_temp.prismedia_playable_map AS item_map ON item_map.old_id = item.item_entity_id
        WHERE collection_map.old_id IS NOT NULL OR item_map.old_id IS NOT NULL;

        DO $prismedia$
        BEGIN
            IF EXISTS (
                SELECT collection_entity_id, item_entity_id
                FROM (
                    SELECT * FROM pg_temp.prismedia_collection_item_projection
                    UNION ALL
                    SELECT item.* FROM collection_item_details AS item
                    WHERE NOT EXISTS (
                        SELECT 1 FROM pg_temp.prismedia_playable_map AS map
                        WHERE map.old_id = item.collection_entity_id OR map.old_id = item.item_entity_id)
                ) AS projected
                GROUP BY collection_entity_id, item_entity_id
                HAVING count(DISTINCT jsonb_build_object(
                    'source', source, 'sortOrder', sort_order, 'addedAt', added_at)) > 1
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found conflicting collection item projections';
            END IF;
        END
        $prismedia$;

        DELETE FROM collection_item_details AS item
        WHERE EXISTS (
            SELECT 1 FROM pg_temp.prismedia_playable_map AS map
            WHERE map.old_id = item.collection_entity_id OR map.old_id = item.item_entity_id);

        INSERT INTO collection_item_details (id, collection_entity_id, item_entity_id, source, sort_order, added_at)
        SELECT DISTINCT ON (collection_entity_id, item_entity_id)
            id, collection_entity_id, item_entity_id, source, sort_order, added_at
        FROM pg_temp.prismedia_collection_item_projection
        ORDER BY collection_entity_id, item_entity_id, added_at, id;

        UPDATE collection_details AS row SET cover_item_entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.cover_item_entity_id = map.old_id;
        UPDATE gallery_details AS row SET cover_image_entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.cover_image_entity_id = map.old_id;
        UPDATE book_details AS row SET cover_page_entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.cover_page_entity_id = map.old_id;
        UPDATE book_chapter_details AS row SET cover_page_entity_id = map.new_id FROM pg_temp.prismedia_playable_map AS map WHERE map.mode = 'collapse' AND row.cover_page_entity_id = map.old_id;

        CREATE OR REPLACE FUNCTION pg_temp.prismedia_expand_video_types(input jsonb)
        RETURNS jsonb
        LANGUAGE plpgsql
        STABLE
        AS $function$
        DECLARE
            result jsonb;
        BEGIN
            IF input IS NULL THEN RETURN NULL; END IF;
            IF jsonb_typeof(input) = 'array' THEN
                SELECT COALESCE(jsonb_agg(pg_temp.prismedia_expand_video_types(value) ORDER BY ordinality), '[]'::jsonb)
                INTO result
                FROM jsonb_array_elements(input) WITH ORDINALITY AS item(value, ordinality);
                RETURN result;
            END IF;
            IF jsonb_typeof(input) <> 'object' THEN RETURN input; END IF;

            SELECT COALESCE(jsonb_object_agg(
                key,
                CASE
                    WHEN key = 'entityTypes' AND jsonb_typeof(value) = 'array' THEN (
                        SELECT COALESCE(jsonb_agg(expanded.value ORDER BY expanded.first_order), '[]'::jsonb)
                        FROM (
                            SELECT value, min(sort_order) AS first_order
                            FROM (
                                SELECT CASE branch
                                           WHEN 0 THEN item.value
                                           WHEN 1 THEN to_jsonb('__VIDEO_EPISODE_KIND__'::text)
                                           ELSE to_jsonb('__MOVIE_KIND__'::text)
                                       END AS value,
                                       item.ordinality * 10 + branch AS sort_order
                                FROM jsonb_array_elements(value) WITH ORDINALITY AS item(value, ordinality)
                                CROSS JOIN LATERAL generate_series(
                                    0,
                                    CASE WHEN item.value = to_jsonb('__VIDEO_KIND__'::text) THEN 2 ELSE 0 END
                                ) AS branch
                            ) AS emitted
                            GROUP BY value
                        ) AS expanded
                    )
                    ELSE pg_temp.prismedia_expand_video_types(value)
                END), '{}'::jsonb)
            INTO result
            FROM jsonb_each(input);
            RETURN result;
        END
        $function$;

        UPDATE collection_details
        SET rule_tree_json = pg_temp.prismedia_expand_video_types(rule_tree_json)
        WHERE rule_tree_json IS DISTINCT FROM pg_temp.prismedia_expand_video_types(rule_tree_json);

        UPDATE app_settings
        SET value_json = jsonb_set(
                value_json,
                ARRAY['__VIDEO_EPISODE_KIND__'],
                value_json -> '__VIDEO_KIND__',
                true),
            updated_at = now()
        WHERE key = '__DEFAULT_PROVIDERS_KEY__'
          AND jsonb_typeof(value_json) = 'object'
          AND value_json ? '__VIDEO_KIND__'
          AND NOT value_json ? '__VIDEO_EPISODE_KIND__';

        CREATE OR REPLACE FUNCTION pg_temp.prismedia_job_json_mapped_kinds(input jsonb)
        RETURNS text[]
        LANGUAGE sql
        STABLE
        AS $function$
            WITH RECURSIVE nodes(value) AS (
                SELECT input
                UNION ALL
                SELECT child.value
                FROM nodes
                CROSS JOIN LATERAL (
                    SELECT object_child.value
                    FROM jsonb_each(
                        CASE WHEN jsonb_typeof(nodes.value) = 'object'
                             THEN nodes.value ELSE '{}'::jsonb END) AS object_child(key, value)
                    UNION ALL
                    SELECT array_child.value
                    FROM jsonb_array_elements(
                        CASE WHEN jsonb_typeof(nodes.value) = 'array'
                             THEN nodes.value ELSE '[]'::jsonb END) AS array_child(value)
                ) AS child
            ), mapped AS (
                SELECT map.new_kind
                FROM nodes
                CROSS JOIN LATERAL jsonb_each(
                    CASE WHEN jsonb_typeof(nodes.value) = 'object'
                         THEN nodes.value ELSE '{}'::jsonb END) AS member(key, value)
                INNER JOIN pg_temp.prismedia_playable_map AS map
                    ON (member.key IN ('EntityId', 'VideoId', 'RootEntityId', 'TargetEntityId', 'ParentEntityId')
                        AND jsonb_typeof(member.value) = 'string'
                        AND member.value #>> '{}' = map.old_id::text)
                    OR (member.key = 'EntityIds'
                        AND jsonb_typeof(member.value) = 'array'
                        AND EXISTS (
                            SELECT 1
                            FROM jsonb_array_elements(member.value) AS item(value)
                            WHERE jsonb_typeof(item.value) = 'string'
                              AND item.value #>> '{}' = map.old_id::text))
            )
            SELECT COALESCE(array_agg(DISTINCT new_kind ORDER BY new_kind), ARRAY[]::text[])
            FROM mapped
        $function$;

        CREATE OR REPLACE FUNCTION pg_temp.prismedia_job_json_has_ambiguous_kind(input jsonb)
        RETURNS boolean
        LANGUAGE plpgsql
        STABLE
        AS $function$
        DECLARE
            child jsonb;
        BEGIN
            IF input IS NULL THEN RETURN false; END IF;
            IF jsonb_typeof(input) = 'object' THEN
                IF EXISTS (
                    SELECT 1
                    FROM jsonb_each(input) AS member(key, value)
                    WHERE member.key IN ('EntityKind', 'TargetEntityKind', 'RootEntityKind')
                      AND member.value = to_jsonb('__VIDEO_KIND__'::text)
                ) AND cardinality(pg_temp.prismedia_job_json_mapped_kinds(input)) > 1 THEN
                    RETURN true;
                END IF;
                FOR child IN SELECT value FROM jsonb_each(input) LOOP
                    IF pg_temp.prismedia_job_json_has_ambiguous_kind(child) THEN RETURN true; END IF;
                END LOOP;
            ELSIF jsonb_typeof(input) = 'array' THEN
                FOR child IN SELECT value FROM jsonb_array_elements(input) LOOP
                    IF pg_temp.prismedia_job_json_has_ambiguous_kind(child) THEN RETURN true; END IF;
                END LOOP;
            END IF;
            RETURN false;
        END
        $function$;

        CREATE OR REPLACE FUNCTION pg_temp.prismedia_retarget_job_json(input jsonb)
        RETURNS jsonb
        LANGUAGE plpgsql
        STABLE
        AS $function$
        DECLARE
            result jsonb;
            inferred_kind text;
            mapped_kinds text[];
        BEGIN
            IF input IS NULL THEN RETURN NULL; END IF;
            IF jsonb_typeof(input) = 'array' THEN
                SELECT COALESCE(jsonb_agg(pg_temp.prismedia_retarget_job_json(value) ORDER BY ordinality), '[]'::jsonb)
                INTO result
                FROM jsonb_array_elements(input) WITH ORDINALITY AS item(value, ordinality);
                RETURN result;
            END IF;
            IF jsonb_typeof(input) <> 'object' THEN RETURN input; END IF;

            mapped_kinds := pg_temp.prismedia_job_json_mapped_kinds(input);
            inferred_kind := CASE WHEN cardinality(mapped_kinds) = 1 THEN mapped_kinds[1] ELSE NULL END;

            SELECT COALESCE(jsonb_object_agg(
                key,
                CASE
                    WHEN key IN ('EntityId', 'VideoId', 'RootEntityId', 'TargetEntityId', 'ParentEntityId')
                         AND jsonb_typeof(value) = 'string' THEN
                        COALESCE((
                            SELECT to_jsonb(map.new_id::text)
                            FROM pg_temp.prismedia_playable_map AS map
                            WHERE map.old_id::text = value #>> '{}'
                        ), value)
                    WHEN key = 'EntityIds' AND jsonb_typeof(value) = 'array' THEN (
                        SELECT COALESCE(jsonb_agg(
                            COALESCE((
                                SELECT to_jsonb(map.new_id::text)
                                FROM pg_temp.prismedia_playable_map AS map
                                WHERE jsonb_typeof(item.value) = 'string'
                                  AND map.old_id::text = item.value #>> '{}'
                            ), item.value)
                            ORDER BY item.ordinality), '[]'::jsonb)
                        FROM jsonb_array_elements(value) WITH ORDINALITY AS item(value, ordinality)
                    )
                    WHEN key IN ('EntityKind', 'TargetEntityKind', 'RootEntityKind')
                         AND inferred_kind IS NOT NULL
                         AND value = to_jsonb('__VIDEO_KIND__'::text) THEN to_jsonb(inferred_kind)
                    ELSE pg_temp.prismedia_retarget_job_json(value)
                END), '{}'::jsonb)
            INTO result
            FROM jsonb_each(input);
            RETURN result;
        END
        $function$;

        CREATE TEMP TABLE prismedia_affected_job_runs (id uuid PRIMARY KEY) ON COMMIT DROP;
        INSERT INTO pg_temp.prismedia_affected_job_runs (id)
        SELECT run.id
        FROM job_runs AS run
        WHERE EXISTS (
                  SELECT 1 FROM pg_temp.prismedia_playable_map AS map
                  WHERE run.target_entity_id = map.old_id::text
                     OR run.resource_key = '__ENTITY_RESOURCE_PREFIX__' || map.old_id::text
                     OR run.node_key = run.type || ':' || map.old_id::text)
           OR run.payload_json IS DISTINCT FROM pg_temp.prismedia_retarget_job_json(run.payload_json);

        CREATE TEMP TABLE prismedia_affected_job_graphs (id uuid PRIMARY KEY) ON COMMIT DROP;
        INSERT INTO pg_temp.prismedia_affected_job_graphs (id)
        SELECT graph.id
        FROM job_graphs AS graph
        LEFT JOIN job_runs AS root_run ON root_run.id = graph.root_run_id
        WHERE EXISTS (
            SELECT 1 FROM pg_temp.prismedia_playable_map AS map
            WHERE graph.root_entity_id = map.old_id::text
               OR graph.active_key = root_run.type || ':' || map.old_id::text);

        DO $prismedia$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM job_runs
                WHERE pg_temp.prismedia_job_json_has_ambiguous_kind(payload_json)
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found a job payload whose legacy Video kind spans mixed direct-playable identities';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM job_runs AS run
                WHERE run.status = '__JOB_RUNNING__'
                  AND (EXISTS (
                           SELECT 1 FROM pg_temp.prismedia_playable_map AS map
                           WHERE run.target_entity_id = map.old_id::text
                              OR run.resource_key = '__ENTITY_RESOURCE_PREFIX__' || map.old_id::text)
                       OR run.payload_json IS DISTINCT FROM pg_temp.prismedia_retarget_job_json(run.payload_json))
            ) OR EXISTS (
                SELECT 1
                FROM job_graphs AS graph
                WHERE graph.status = '__GRAPH_RUNNING__'
                  AND EXISTS (
                      SELECT 1 FROM pg_temp.prismedia_playable_map AS map
                      WHERE graph.root_entity_id = map.old_id::text)
            ) OR EXISTS (
                SELECT 1
                FROM job_resource_leases AS lease
                INNER JOIN pg_temp.prismedia_playable_map AS map
                    ON lease.resource_key = '__ENTITY_RESOURCE_PREFIX__' || map.old_id::text
                WHERE lease.expires_at > now()
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found running or leased job work';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM job_graphs AS graph
                INNER JOIN job_runs AS root_run ON root_run.id = graph.root_run_id
                INNER JOIN pg_temp.prismedia_playable_map AS map
                    ON graph.active_key = root_run.type || ':' || map.old_id::text
                INNER JOIN job_graphs AS conflicting
                    ON conflicting.id <> graph.id
                   AND conflicting.active_key = root_run.type || ':' || map.new_id::text
                   AND conflicting.status IN ('__GRAPH_QUEUED__', '__GRAPH_RUNNING__', '__GRAPH_WAITING__')
                WHERE graph.status IN ('__GRAPH_QUEUED__', '__GRAPH_RUNNING__', '__GRAPH_WAITING__')
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found a conflicting active job graph key';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM job_runs AS run
                INNER JOIN pg_temp.prismedia_playable_map AS map
                    ON run.node_key = run.type || ':' || map.old_id::text
                INNER JOIN job_runs AS conflicting
                    ON conflicting.graph_id = run.graph_id
                   AND conflicting.id <> run.id
                   AND conflicting.node_key = run.type || ':' || map.new_id::text
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found a conflicting job graph node key';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM job_resource_states AS state
                INNER JOIN pg_temp.prismedia_playable_map AS map
                    ON state.key = '__ENTITY_RESOURCE_PREFIX__' || map.old_id::text
                INNER JOIN job_resource_states AS conflicting
                    ON conflicting.key = '__ENTITY_RESOURCE_PREFIX__' || map.new_id::text
                WHERE map.old_id <> map.new_id
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration found conflicting Entity job resource states';
            END IF;
        END
        $prismedia$;

        UPDATE job_runs AS run
        SET payload_json = pg_temp.prismedia_retarget_job_json(run.payload_json),
            target_entity_id = map.new_id::text,
            target_entity_kind = map.new_kind,
            node_key = CASE
                WHEN run.node_key = run.type || ':' || map.old_id::text
                    THEN run.type || ':' || map.new_id::text
                ELSE run.node_key
            END
        FROM pg_temp.prismedia_playable_map AS map
        WHERE run.target_entity_id = map.old_id::text;

        UPDATE job_runs AS run
        SET payload_json = pg_temp.prismedia_retarget_job_json(run.payload_json)
        WHERE run.payload_json IS DISTINCT FROM pg_temp.prismedia_retarget_job_json(run.payload_json);

        UPDATE job_runs AS run
        SET node_key = run.type || ':' || map.new_id::text
        FROM pg_temp.prismedia_playable_map AS map
        WHERE run.node_key = run.type || ':' || map.old_id::text;

        UPDATE job_graphs AS graph
        SET root_entity_id = map.new_id::text,
            root_entity_kind = map.new_kind,
            active_key = CASE
                WHEN graph.active_key = root_run.type || ':' || map.old_id::text
                    THEN root_run.type || ':' || map.new_id::text
                ELSE graph.active_key
            END
        FROM job_runs AS root_run, pg_temp.prismedia_playable_map AS map
        WHERE graph.root_run_id = root_run.id AND graph.root_entity_id = map.old_id::text;

        INSERT INTO job_resource_states (key, max_concurrency, minimum_start_interval_ms, next_available_at, updated_at)
        SELECT '__ENTITY_RESOURCE_PREFIX__' || map.new_id::text,
               state.max_concurrency,
               state.minimum_start_interval_ms,
               state.next_available_at,
               state.updated_at
        FROM job_resource_states AS state
        INNER JOIN pg_temp.prismedia_playable_map AS map
            ON state.key = '__ENTITY_RESOURCE_PREFIX__' || map.old_id::text
        WHERE map.old_id <> map.new_id;

        UPDATE job_resource_leases AS lease
        SET resource_key = '__ENTITY_RESOURCE_PREFIX__' || map.new_id::text
        FROM pg_temp.prismedia_playable_map AS map
        WHERE lease.resource_key = '__ENTITY_RESOURCE_PREFIX__' || map.old_id::text
          AND map.old_id <> map.new_id;

        UPDATE job_runs AS run
        SET resource_key = '__ENTITY_RESOURCE_PREFIX__' || map.new_id::text
        FROM pg_temp.prismedia_playable_map AS map
        WHERE run.resource_key = '__ENTITY_RESOURCE_PREFIX__' || map.old_id::text
          AND map.old_id <> map.new_id;

        DELETE FROM job_resource_states AS state
        USING pg_temp.prismedia_playable_map AS map
        WHERE state.key = '__ENTITY_RESOURCE_PREFIX__' || map.old_id::text
          AND map.old_id <> map.new_id;

        DO $prismedia$
        BEGIN
            IF EXISTS (
                SELECT 1
                FROM job_runs AS run
                INNER JOIN pg_temp.prismedia_affected_job_runs AS affected ON affected.id = run.id
                WHERE EXISTS (
                    SELECT 1
                    FROM pg_temp.prismedia_playable_map AS map
                    WHERE (map.mode = 'collapse' AND run.target_entity_id = map.old_id::text)
                       OR (map.mode = 'collapse' AND run.resource_key = '__ENTITY_RESOURCE_PREFIX__' || map.old_id::text)
                       OR (map.mode = 'collapse' AND run.node_key = run.type || ':' || map.old_id::text))
                   OR run.payload_json IS DISTINCT FROM pg_temp.prismedia_retarget_job_json(run.payload_json)
                   OR EXISTS (
                       SELECT 1
                       FROM pg_temp.prismedia_playable_map AS map
                       WHERE run.target_entity_id = map.new_id::text
                         AND run.target_entity_kind IS DISTINCT FROM map.new_kind)
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration left an inconsistent affected job run reference';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM job_graphs AS graph
                INNER JOIN pg_temp.prismedia_affected_job_graphs AS affected ON affected.id = graph.id
                LEFT JOIN job_runs AS root_run ON root_run.id = graph.root_run_id
                WHERE EXISTS (
                    SELECT 1
                    FROM pg_temp.prismedia_playable_map AS map
                    WHERE (map.mode = 'collapse' AND graph.root_entity_id = map.old_id::text)
                       OR (map.mode = 'collapse' AND graph.active_key = root_run.type || ':' || map.old_id::text))
                   OR EXISTS (
                       SELECT 1
                       FROM pg_temp.prismedia_playable_map AS map
                       WHERE graph.root_entity_id = map.new_id::text
                         AND graph.root_entity_kind IS DISTINCT FROM map.new_kind)
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration left an inconsistent affected job graph reference';
            END IF;

            IF EXISTS (
                SELECT 1
                FROM job_resource_states AS state
                INNER JOIN pg_temp.prismedia_playable_map AS map
                    ON state.key = '__ENTITY_RESOURCE_PREFIX__' || map.old_id::text
                WHERE map.mode = 'collapse'
            ) OR EXISTS (
                SELECT 1
                FROM job_resource_leases AS lease
                INNER JOIN pg_temp.prismedia_playable_map AS map
                    ON lease.resource_key = '__ENTITY_RESOURCE_PREFIX__' || map.old_id::text
                WHERE map.mode = 'collapse'
            ) THEN
                RAISE EXCEPTION 'Direct-playable migration left a legacy Entity job resource reference';
            END IF;
        END
        $prismedia$;

        UPDATE acquisitions AS row
        SET entity_id = map.new_id, kind = map.new_kind
        FROM pg_temp.prismedia_playable_map AS map
        WHERE row.entity_id = map.old_id;
        UPDATE acquisition_history AS row
        SET entity_id = map.new_id, kind = map.new_kind
        FROM pg_temp.prismedia_playable_map AS map
        WHERE row.entity_id = map.old_id;
        UPDATE acquisition_import_hints AS row
        SET entity_id = map.new_id
        FROM pg_temp.prismedia_playable_map AS map
        WHERE row.entity_id = map.old_id;
        UPDATE monitors AS row
        SET entity_id = map.new_id, kind = map.new_kind
        FROM pg_temp.prismedia_playable_map AS map
        WHERE row.entity_id = map.old_id;

        UPDATE entities AS entity
        SET kind_code = map.new_kind, updated_at = GREATEST(entity.updated_at, now())
        FROM pg_temp.prismedia_playable_map AS map
        WHERE map.mode = 'episode' AND entity.id = map.old_id;

        DELETE FROM video_details AS detail
        USING pg_temp.prismedia_playable_map AS map
        WHERE detail.entity_id = map.old_id;

        DO $prismedia$
        DECLARE
            reference record;
            remaining bigint;
        BEGIN
            FOR reference IN
                SELECT
                    constraint_table.relname AS table_name,
                    attribute.attname AS column_name
                FROM pg_constraint AS constraint_row
                INNER JOIN pg_class AS constraint_table ON constraint_table.oid = constraint_row.conrelid
                INNER JOIN pg_namespace AS namespace_row ON namespace_row.oid = constraint_table.relnamespace
                INNER JOIN unnest(constraint_row.conkey) AS key(attnum) ON true
                INNER JOIN pg_attribute AS attribute
                    ON attribute.attrelid = constraint_row.conrelid AND attribute.attnum = key.attnum
                WHERE constraint_row.contype = 'f'
                  AND constraint_row.confrelid = 'entities'::regclass
                  AND namespace_row.nspname = current_schema()
                  AND cardinality(constraint_row.conkey) = 1
                  AND NOT (constraint_table.relname = 'video_details' AND attribute.attname = 'entity_id')
            LOOP
                EXECUTE format(
                    'SELECT count(*) FROM %I WHERE %I IN (SELECT old_id FROM pg_temp.prismedia_playable_map WHERE mode = ''collapse'')',
                    reference.table_name,
                    reference.column_name)
                INTO remaining;
                IF remaining > 0 THEN
                    RAISE EXCEPTION 'Direct-playable migration left % reference(s) in %.%',
                        remaining, reference.table_name, reference.column_name;
                END IF;
            END LOOP;
        END
        $prismedia$;

        DELETE FROM entities AS entity
        USING pg_temp.prismedia_playable_map AS map
        WHERE map.mode = 'collapse' AND entity.id = map.old_id;

        DROP TABLE video_details;
        """
        .Replace("__ADVISORY_LOCK__", DirectPlayableMigrationAssetPreparer.AdvisoryLockName, StringComparison.Ordinal)
        .Replace("__MANIFEST_TABLE__", DirectPlayableMigrationAssetPreparer.ManifestTable, StringComparison.Ordinal)
        .Replace("__MOVIE_MAPPING_SUBJECT__", DirectPlayableMigrationAssetPreparer.MovieMappingSubject, StringComparison.Ordinal)
        .Replace("__MOVIE_PAYLOAD_SUBJECT__", DirectPlayableMigrationAssetPreparer.MoviePayloadSubject, StringComparison.Ordinal)
        .Replace("__SOURCE_CLASSIFICATION_SUBJECT__", DirectPlayableMigrationAssetPreparer.SourceClassificationSubject, StringComparison.Ordinal)
        .Replace("__SUBTITLE_SUBJECT__", DirectPlayableMigrationAssetPreparer.SubtitleSubject, StringComparison.Ordinal)
        .Replace("__ENTITY_FILE_SUBJECT__", DirectPlayableMigrationAssetPreparer.EntityFileSubject, StringComparison.Ordinal)
        .Replace("__LIBRARY_ROOT_BACKFILL_SUBJECT__", DirectPlayableMigrationAssetPreparer.LibraryRootBackfillSubject, StringComparison.Ordinal)
        .Replace("__LIBRARY_ROOT_SNAPSHOT_SUBJECT__", DirectPlayableMigrationAssetPreparer.LibraryRootSnapshotSubject, StringComparison.Ordinal)
        .Replace("__FILE_CLASS__", DirectPlayableMigrationAssetPreparer.FileClassification, StringComparison.Ordinal)
        .Replace("__FOLDER_CLASS__", DirectPlayableMigrationAssetPreparer.FolderClassification, StringComparison.Ordinal)
        .Replace("__VIDEO_KIND__", EntityKind.Video.ToCode(), StringComparison.Ordinal)
        .Replace("__MOVIE_KIND__", EntityKind.Movie.ToCode(), StringComparison.Ordinal)
        .Replace("__VIDEO_EPISODE_KIND__", EntityKind.VideoEpisode.ToCode(), StringComparison.Ordinal)
        .Replace("__VIDEO_SERIES_KIND__", EntityKind.VideoSeries.ToCode(), StringComparison.Ordinal)
        .Replace("__VIDEO_SEASON_KIND__", EntityKind.VideoSeason.ToCode(), StringComparison.Ordinal)
        .Replace("__GALLERY_KIND__", EntityKind.Gallery.ToCode(), StringComparison.Ordinal)
        .Replace("__IMAGE_KIND__", EntityKind.Image.ToCode(), StringComparison.Ordinal)
        .Replace("__AUDIO_TRACK_KIND__", EntityKind.AudioTrack.ToCode(), StringComparison.Ordinal)
        .Replace("__AUDIO_LIBRARY_KIND__", EntityKind.AudioLibrary.ToCode(), StringComparison.Ordinal)
        .Replace("__MUSIC_ARTIST_KIND__", EntityKind.MusicArtist.ToCode(), StringComparison.Ordinal)
        .Replace("__BOOK_AUTHOR_KIND__", EntityKind.BookAuthor.ToCode(), StringComparison.Ordinal)
        .Replace("__BOOK_KIND__", EntityKind.Book.ToCode(), StringComparison.Ordinal)
        .Replace("__BOOK_VOLUME_KIND__", EntityKind.BookVolume.ToCode(), StringComparison.Ordinal)
        .Replace("__MEDIA_CATEGORY__", EntityKindCategory.Media.ToString(), StringComparison.Ordinal)
        .Replace("__FILE_SHAPE__", EntityStorageShape.File.ToCode(), StringComparison.Ordinal)
        .Replace("__SOURCE_ROLE__", EntityFileRole.Source.ToCode(), StringComparison.Ordinal)
        .Replace("__THUMBNAIL_ROLE__", EntityFileRole.Thumbnail.ToCode(), StringComparison.Ordinal)
        .Replace("__GRID_THUMBNAIL_ROLE__", EntityFileRole.GridThumbnail.ToCode(), StringComparison.Ordinal)
        .Replace("__GRID_THUMBNAIL_2X_ROLE__", EntityFileRole.GridThumbnail2x.ToCode(), StringComparison.Ordinal)
        .Replace("__PREVIEW_ROLE__", EntityFileRole.Preview.ToCode(), StringComparison.Ordinal)
        .Replace("__SPRITE_ROLE__", EntityFileRole.Sprite.ToCode(), StringComparison.Ordinal)
        .Replace("__TRICKPLAY_ROLE__", EntityFileRole.Trickplay.ToCode(), StringComparison.Ordinal)
        .Replace("__HLS_ROLE__", EntityFileRole.Hls.ToCode(), StringComparison.Ordinal)
        .Replace("__FOLDER_SOURCE_CODE__", EntitySourceCode.Folder.ToCode(), StringComparison.Ordinal)
        .Replace("__SCAN_SOURCE__", FileSourceKind.Scan.ToCode(), StringComparison.Ordinal)
        .Replace("__LEGACY_PERFORMER_ROLE__", LegacyPerformerRoleCode, StringComparison.Ordinal)
        .Replace("__ACTOR_ROLE__", CreditRole.Actor.ToCode(), StringComparison.Ordinal)
        .Replace("__CAST_RELATIONSHIP__", RelationshipKind.Cast.ToCode(), StringComparison.Ordinal)
        .Replace("__CREDITS_RELATIONSHIP__", RelationshipKind.Credits.ToCode(), StringComparison.Ordinal)
        .Replace("__DEFAULT_PROVIDERS_KEY__", AppSettings.Identify.DefaultProviders.Key, StringComparison.Ordinal)
        .Replace("__IDENTIFY_DONE__", IdentifyQueueState.Done.ToCode(), StringComparison.Ordinal)
        .Replace("__IDENTIFY_DELETED__", IdentifyQueueState.Deleted.ToCode(), StringComparison.Ordinal)
        .Replace("__ACQUISITION_SEARCHING__", AcquisitionStatus.Searching.ToCode(), StringComparison.Ordinal)
        .Replace("__ACQUISITION_QUEUED__", AcquisitionStatus.Queued.ToCode(), StringComparison.Ordinal)
        .Replace("__ACQUISITION_DOWNLOADING__", AcquisitionStatus.Downloading.ToCode(), StringComparison.Ordinal)
        .Replace("__ACQUISITION_WAITING_CLIENT__", AcquisitionStatus.WaitingForDownloadClient.ToCode(), StringComparison.Ordinal)
        .Replace("__ACQUISITION_DOWNLOADED__", AcquisitionStatus.Downloaded.ToCode(), StringComparison.Ordinal)
        .Replace("__ACQUISITION_IMPORTING__", AcquisitionStatus.Importing.ToCode(), StringComparison.Ordinal)
        .Replace("__ACQUISITION_STOPPING__", AcquisitionStatus.Stopping.ToCode(), StringComparison.Ordinal)
        .Replace("__MONITOR_DELETING__", MonitorStatus.DeletingFiles.ToCode(), StringComparison.Ordinal)
        .Replace("__MONITOR_STOPPING__", MonitorStatus.Stopping.ToCode(), StringComparison.Ordinal)
        .Replace("__JOB_QUEUED__", JobRunStatus.Queued.ToCode(), StringComparison.Ordinal)
        .Replace("__JOB_RUNNING__", JobRunStatus.Running.ToCode(), StringComparison.Ordinal)
        .Replace("__JOB_CANCELLED__", JobRunStatus.Cancelled.ToCode(), StringComparison.Ordinal)
        .Replace("__GRAPH_QUEUED__", JobGraphStatus.Queued.ToCode(), StringComparison.Ordinal)
        .Replace("__GRAPH_RUNNING__", JobGraphStatus.Running.ToCode(), StringComparison.Ordinal)
        .Replace("__GRAPH_WAITING__", JobGraphStatus.Waiting.ToCode(), StringComparison.Ordinal)
        .Replace("__GRAPH_CANCELLED__", JobGraphStatus.Cancelled.ToCode(), StringComparison.Ordinal)
        .Replace("__ENTITY_RESOURCE_PREFIX__", JobResourceKeys.EntityPrefix, StringComparison.Ordinal);
}
