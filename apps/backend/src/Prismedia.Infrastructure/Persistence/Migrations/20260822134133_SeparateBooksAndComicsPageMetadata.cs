using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeparateBooksAndComicsPageMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "page_count",
                table: "comic_installment_details",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "page_count",
                table: "book_details",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "page_count",
                table: "book_chapter_details",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(
                """
                -- Classify the old image-archive Book roots before changing any kind codes. A folder
                -- source is a serialized title; a direct CBZ/ZIP source is a one-shot installment.
                CREATE TEMP TABLE prismedia_legacy_comic_books ON COMMIT DROP AS
                SELECT
                    entity.id,
                    EXISTS (
                        SELECT 1
                        FROM entity_sources AS source
                        WHERE source.entity_id = entity.id
                          AND source.code = 'folder'
                    ) AS is_series,
                    detail.book_type
                FROM entities AS entity
                INNER JOIN book_details AS detail ON detail.entity_id = entity.id
                WHERE entity.kind_code = 'book'
                  AND detail.format = 'image-archive';

                CREATE UNIQUE INDEX ON prismedia_legacy_comic_books (id);

                -- A root-level archive previously used the Book id as both the work and its readable
                -- payload. Keep that id for the installment and create only the missing series wrapper.
                CREATE TEMP TABLE prismedia_root_archive_series ON COMMIT DROP AS
                SELECT legacy.id AS installment_id, gen_random_uuid() AS series_id
                FROM prismedia_legacy_comic_books AS legacy
                WHERE NOT legacy.is_series;

                CREATE UNIQUE INDEX ON prismedia_root_archive_series (installment_id);
                CREATE UNIQUE INDEX ON prismedia_root_archive_series (series_id);

                INSERT INTO entities (
                    id,
                    kind_code,
                    title,
                    parent_entity_id,
                    sort_order,
                    is_nsfw,
                    is_organized,
                    is_wanted,
                    lifecycle_claim_kind,
                    lifecycle_claim_id,
                    lifecycle_claimed_at,
                    auto_identify_attempts,
                    created_at,
                    updated_at)
                SELECT
                    mapping.series_id,
                    'comic-series',
                    installment.title,
                    NULL,
                    NULL,
                    installment.is_nsfw,
                    installment.is_organized,
                    installment.is_wanted,
                    NULL,
                    NULL,
                    NULL,
                    0,
                    installment.created_at,
                    installment.updated_at
                FROM prismedia_root_archive_series AS mapping
                INNER JOIN entities AS installment ON installment.id = mapping.installment_id;

                INSERT INTO comic_series_details (entity_id, status)
                SELECT mapping.series_id, NULL
                FROM prismedia_root_archive_series AS mapping;

                INSERT INTO entity_library_roots (entity_id, library_root_id)
                SELECT mapping.series_id, rooted.library_root_id
                FROM prismedia_root_archive_series AS mapping
                INNER JOIN entity_library_roots AS rooted ON rooted.entity_id = mapping.installment_id
                ON CONFLICT (entity_id) DO UPDATE
                SET library_root_id = EXCLUDED.library_root_id;

                -- Preserve top-level favorite, rating, activity, and reading state on the new series.
                INSERT INTO user_entity_states (
                    user_id, entity_id, is_favorite, rating_value, access_count, completion_count,
                    skip_count, active_seconds, resume_seconds, last_accessed_at, last_active_at,
                    completed_at, progress_current_entity_id, progress_unit, progress_index,
                    progress_total, progress_mode, progress_location, progress_completed_at,
                    progress_updated_at, progress_consumed_count, updated_at)
                SELECT
                    state.user_id, mapping.series_id, state.is_favorite, state.rating_value,
                    state.access_count, state.completion_count, state.skip_count, state.active_seconds,
                    state.resume_seconds, state.last_accessed_at, state.last_active_at,
                    state.completed_at, state.progress_current_entity_id, state.progress_unit,
                    state.progress_index, state.progress_total, state.progress_mode,
                    state.progress_location, state.progress_completed_at, state.progress_updated_at,
                    state.progress_consumed_count, state.updated_at
                FROM prismedia_root_archive_series AS mapping
                INNER JOIN user_entity_states AS state ON state.entity_id = mapping.installment_id;

                -- Resolve every old chapter that becomes (or feeds) a ComicInstallment. Chapters in a
                -- folder-backed hierarchy keep their ids. The synthetic chapter beneath a one-shot is
                -- folded into the archive-owning Book id so the source remains direct and stable.
                CREATE TEMP TABLE prismedia_legacy_comic_installments ON COMMIT DROP AS
                SELECT
                    chapter.id AS old_chapter_id,
                    chapter.id AS installment_id,
                    series.id AS series_id,
                    legacy.book_type,
                    'chapter'::text AS installment_kind
                FROM entities AS chapter
                INNER JOIN entities AS parent ON parent.id = chapter.parent_entity_id
                INNER JOIN entities AS series
                    ON series.id = CASE
                        WHEN parent.kind_code = 'book' THEN parent.id
                        WHEN parent.kind_code = 'book-volume' THEN parent.parent_entity_id
                        ELSE NULL
                    END
                INNER JOIN prismedia_legacy_comic_books AS legacy
                    ON legacy.id = series.id AND legacy.is_series
                WHERE chapter.kind_code = 'book-chapter'
                UNION ALL
                SELECT
                    COALESCE(chapter.id, mapping.installment_id) AS old_chapter_id,
                    mapping.installment_id,
                    mapping.series_id,
                    legacy.book_type,
                    'one-shot'::text AS installment_kind
                FROM prismedia_root_archive_series AS mapping
                INNER JOIN prismedia_legacy_comic_books AS legacy
                    ON legacy.id = mapping.installment_id
                LEFT JOIN entities AS chapter
                    ON chapter.parent_entity_id = mapping.installment_id
                   AND chapter.kind_code = 'book-chapter';

                CREATE INDEX ON prismedia_legacy_comic_installments (old_chapter_id);
                CREATE INDEX ON prismedia_legacy_comic_installments (installment_id);

                -- Snapshot the old page rows as manifest resources. Their source file stores the
                -- canonical archive/member pair as "archive-path::member".
                CREATE TEMP TABLE prismedia_legacy_comic_pages ON COMMIT DROP AS
                SELECT
                    installment.installment_id,
                    page.id AS old_page_id,
                    row_number() OVER (
                        PARTITION BY installment.installment_id
                        ORDER BY page.sort_order NULLS LAST, page.id) - 1 AS ordinal,
                    substring(file.path FROM position('::' IN file.path) + 2) AS archive_member,
                    COALESCE(
                        NULLIF(file.mime_type, ''),
                        CASE lower(split_part(file.path, '.', -1))
                            WHEN 'png' THEN 'image/png'
                            WHEN 'webp' THEN 'image/webp'
                            WHEN 'gif' THEN 'image/gif'
                            ELSE 'image/jpeg'
                        END) AS mime_type,
                    chapter_detail.cover_page_entity_id
                FROM prismedia_legacy_comic_installments AS installment
                INNER JOIN entities AS chapter ON chapter.id = installment.old_chapter_id
                INNER JOIN book_chapter_details AS chapter_detail ON chapter_detail.entity_id = chapter.id
                INNER JOIN entities AS page
                    ON page.parent_entity_id = chapter.id
                   AND page.kind_code = 'book-page'
                INNER JOIN entity_files AS file
                    ON file.entity_id = page.id
                   AND file.role = 'source'
                   AND position('::' IN file.path) > 0;

                CREATE INDEX ON prismedia_legacy_comic_pages (installment_id, ordinal);

                -- Convert folder-backed hierarchy nodes in place so metadata, relationships, and
                -- saved ids remain attached to the same released works.
                UPDATE entities AS entity
                SET kind_code = 'comic-series'
                FROM prismedia_legacy_comic_books AS legacy
                WHERE entity.id = legacy.id
                  AND legacy.is_series;

                INSERT INTO comic_series_details (entity_id, status)
                SELECT legacy.id, NULL
                FROM prismedia_legacy_comic_books AS legacy
                WHERE legacy.is_series
                ON CONFLICT (entity_id) DO NOTHING;

                UPDATE entities AS volume
                SET kind_code = 'comic-volume'
                FROM prismedia_legacy_comic_books AS legacy
                WHERE legacy.is_series
                  AND volume.kind_code = 'book-volume'
                  AND volume.parent_entity_id = legacy.id;

                UPDATE entities AS chapter
                SET kind_code = 'comic-installment'
                FROM prismedia_legacy_comic_installments AS installment
                WHERE chapter.id = installment.installment_id
                  AND chapter.id = installment.old_chapter_id;

                UPDATE entities AS one_shot
                SET kind_code = 'comic-installment',
                    parent_entity_id = mapping.series_id,
                    sort_order = 0
                FROM prismedia_root_archive_series AS mapping
                WHERE one_shot.id = mapping.installment_id;

                INSERT INTO comic_installment_details (entity_id, installment_kind, page_count)
                SELECT DISTINCT installment_id, installment_kind, 0
                FROM prismedia_legacy_comic_installments
                ON CONFLICT (entity_id) DO UPDATE
                SET installment_kind = EXCLUDED.installment_kind;

                -- Preserve ordinal topology as canonical position metadata.
                INSERT INTO entity_positions (entity_id, code, value, label, updated_at)
                SELECT
                    volume.id,
                    'volume',
                    COALESCE(volume.sort_order, 0),
                    COALESCE(volume.sort_order, 0)::text,
                    volume.updated_at
                FROM entities AS volume
                WHERE volume.kind_code = 'comic-volume'
                  AND EXISTS (
                      SELECT 1
                      FROM prismedia_legacy_comic_books AS legacy
                      WHERE legacy.is_series AND legacy.id = volume.parent_entity_id)
                ON CONFLICT (entity_id, code) DO UPDATE
                SET value = EXCLUDED.value,
                    label = EXCLUDED.label,
                    updated_at = EXCLUDED.updated_at;

                INSERT INTO entity_positions (entity_id, code, value, label, updated_at)
                SELECT DISTINCT
                    installment.installment_id,
                    'chapter',
                    CASE
                        WHEN installment.installment_kind = 'one-shot' THEN 1
                        ELSE COALESCE(entity.sort_order, 0) + 1
                    END,
                    CASE
                        WHEN installment.installment_kind = 'one-shot' THEN '1'
                        ELSE (COALESCE(entity.sort_order, 0) + 1)::text
                    END,
                    entity.updated_at
                FROM prismedia_legacy_comic_installments AS installment
                INNER JOIN entities AS entity ON entity.id = installment.installment_id
                ON CONFLICT (entity_id, code) DO UPDATE
                SET value = EXCLUDED.value,
                    label = EXCLUDED.label,
                    updated_at = EXCLUDED.updated_at;

                -- Materialize the resource manifest before deleting page Entities. Existing manifests
                -- win because a newer comic scan may already have supplied richer ComicInfo semantics.
                INSERT INTO entity_page_manifests (
                    entity_id, direction, default_mode, cover_ordinal, source_signature, updated_at)
                SELECT
                    pages.installment_id,
                    CASE WHEN bool_or(installment.book_type = 'manga')
                        THEN 'right-to-left' ELSE 'left-to-right' END,
                    'paged',
                    COALESCE(
                        min(pages.ordinal) FILTER (WHERE pages.old_page_id = pages.cover_page_entity_id),
                        0),
                    'legacy-pages:' || md5(string_agg(pages.old_page_id::text, ',' ORDER BY pages.ordinal)),
                    now()
                FROM prismedia_legacy_comic_pages AS pages
                INNER JOIN prismedia_legacy_comic_installments AS installment
                    ON installment.installment_id = pages.installment_id
                GROUP BY pages.installment_id
                ON CONFLICT (entity_id) DO NOTHING;

                INSERT INTO entity_page_entries (
                    entity_id, ordinal, archive_member, mime_type, width, height,
                    page_type, is_double_page, checksum)
                SELECT
                    pages.installment_id,
                    pages.ordinal::integer,
                    pages.archive_member,
                    pages.mime_type,
                    NULL,
                    NULL,
                    CASE WHEN pages.ordinal = 0 THEN 'front-cover' ELSE 'story' END,
                    false,
                    NULL
                FROM prismedia_legacy_comic_pages AS pages
                WHERE EXISTS (
                    SELECT 1
                    FROM entity_page_manifests AS manifest
                    WHERE manifest.entity_id = pages.installment_id)
                ON CONFLICT (entity_id, ordinal) DO NOTHING;

                -- Redirect progress that pointed at a folded one-shot chapter before that chapter is
                -- deleted, then promote the same progress onto each installment leaf.
                UPDATE user_entity_states AS state
                SET progress_current_entity_id = installment.installment_id
                FROM prismedia_legacy_comic_installments AS installment
                WHERE state.progress_current_entity_id = installment.old_chapter_id
                  AND installment.old_chapter_id <> installment.installment_id;

                INSERT INTO user_entity_states (
                    user_id, entity_id, is_favorite, rating_value, access_count, completion_count,
                    skip_count, active_seconds, resume_seconds, last_accessed_at, last_active_at,
                    completed_at, progress_current_entity_id, progress_unit, progress_index,
                    progress_total, progress_mode, progress_location, progress_completed_at,
                    progress_updated_at, progress_consumed_count, updated_at)
                SELECT DISTINCT ON (state.user_id, installment.installment_id)
                    state.user_id,
                    installment.installment_id,
                    false,
                    NULL,
                    0,
                    0,
                    0,
                    0,
                    0,
                    NULL,
                    NULL,
                    NULL,
                    installment.installment_id,
                    state.progress_unit,
                    state.progress_index,
                    state.progress_total,
                    state.progress_mode,
                    state.progress_location,
                    state.progress_completed_at,
                    state.progress_updated_at,
                    state.progress_consumed_count,
                    state.updated_at
                FROM user_entity_states AS state
                INNER JOIN prismedia_legacy_comic_installments AS installment
                    ON state.progress_current_entity_id = installment.installment_id
                WHERE state.entity_id <> installment.installment_id
                ORDER BY state.user_id, installment.installment_id, state.progress_updated_at DESC NULLS LAST
                ON CONFLICT (user_id, entity_id) DO UPDATE
                SET progress_current_entity_id = CASE
                        WHEN user_entity_states.progress_updated_at IS NULL
                          OR EXCLUDED.progress_updated_at > user_entity_states.progress_updated_at
                        THEN EXCLUDED.progress_current_entity_id
                        ELSE user_entity_states.progress_current_entity_id
                    END,
                    progress_unit = CASE
                        WHEN user_entity_states.progress_updated_at IS NULL
                          OR EXCLUDED.progress_updated_at > user_entity_states.progress_updated_at
                        THEN EXCLUDED.progress_unit
                        ELSE user_entity_states.progress_unit
                    END,
                    progress_index = CASE
                        WHEN user_entity_states.progress_updated_at IS NULL
                          OR EXCLUDED.progress_updated_at > user_entity_states.progress_updated_at
                        THEN EXCLUDED.progress_index
                        ELSE user_entity_states.progress_index
                    END,
                    progress_total = CASE
                        WHEN user_entity_states.progress_updated_at IS NULL
                          OR EXCLUDED.progress_updated_at > user_entity_states.progress_updated_at
                        THEN EXCLUDED.progress_total
                        ELSE user_entity_states.progress_total
                    END,
                    progress_mode = CASE
                        WHEN user_entity_states.progress_updated_at IS NULL
                          OR EXCLUDED.progress_updated_at > user_entity_states.progress_updated_at
                        THEN EXCLUDED.progress_mode
                        ELSE user_entity_states.progress_mode
                    END,
                    progress_location = CASE
                        WHEN user_entity_states.progress_updated_at IS NULL
                          OR EXCLUDED.progress_updated_at > user_entity_states.progress_updated_at
                        THEN EXCLUDED.progress_location
                        ELSE user_entity_states.progress_location
                    END,
                    progress_completed_at = CASE
                        WHEN user_entity_states.progress_updated_at IS NULL
                          OR EXCLUDED.progress_updated_at > user_entity_states.progress_updated_at
                        THEN EXCLUDED.progress_completed_at
                        ELSE user_entity_states.progress_completed_at
                    END,
                    progress_updated_at = GREATEST(
                        user_entity_states.progress_updated_at,
                        EXCLUDED.progress_updated_at),
                    progress_consumed_count = CASE
                        WHEN user_entity_states.progress_updated_at IS NULL
                          OR EXCLUDED.progress_updated_at > user_entity_states.progress_updated_at
                        THEN EXCLUDED.progress_consumed_count
                        ELSE user_entity_states.progress_consumed_count
                    END,
                    updated_at = GREATEST(user_entity_states.updated_at, EXCLUDED.updated_at);

                -- Cache exact manifest counts on installments and write the same value through the
                -- generic stats capability used by thumbnail contributors.
                UPDATE comic_installment_details AS detail
                SET page_count = counts.page_count
                FROM (
                    SELECT entity_id, count(*)::integer AS page_count
                    FROM entity_page_entries
                    GROUP BY entity_id
                ) AS counts
                WHERE detail.entity_id = counts.entity_id;

                INSERT INTO entity_stats (entity_id, code, value, updated_at)
                SELECT detail.entity_id, 'pages', detail.page_count, now()
                FROM comic_installment_details AS detail
                ON CONFLICT (entity_id, code) DO UPDATE
                SET value = EXCLUDED.value,
                    updated_at = EXCLUDED.updated_at;

                INSERT INTO entity_stats (entity_id, code, value, updated_at)
                SELECT volume.id, 'pages', COALESCE(sum(detail.page_count), 0)::integer, now()
                FROM entities AS volume
                LEFT JOIN entities AS installment
                    ON installment.parent_entity_id = volume.id
                   AND installment.kind_code = 'comic-installment'
                LEFT JOIN comic_installment_details AS detail ON detail.entity_id = installment.id
                WHERE volume.kind_code = 'comic-volume'
                GROUP BY volume.id
                ON CONFLICT (entity_id, code) DO UPDATE
                SET value = EXCLUDED.value,
                    updated_at = EXCLUDED.updated_at;

                INSERT INTO entity_stats (entity_id, code, value, updated_at)
                SELECT series.id, 'pages', COALESCE(sum(detail.page_count), 0)::integer, now()
                FROM entities AS series
                LEFT JOIN entities AS child ON child.parent_entity_id = series.id
                LEFT JOIN entities AS installment
                    ON installment.id = child.id AND child.kind_code = 'comic-installment'
                    OR installment.parent_entity_id = child.id AND child.kind_code = 'comic-volume'
                LEFT JOIN comic_installment_details AS detail ON detail.entity_id = installment.id
                WHERE series.kind_code = 'comic-series'
                GROUP BY series.id
                ON CONFLICT (entity_id, code) DO UPDATE
                SET value = EXCLUDED.value,
                    updated_at = EXCLUDED.updated_at;

                -- Delete only the structural rows that no longer have domain meaning. Cascades remove
                -- their obsolete generated thumbnails and per-page technical rows.
                DELETE FROM entities WHERE kind_code = 'book-page';

                DELETE FROM entities AS chapter
                USING prismedia_legacy_comic_installments AS installment
                WHERE chapter.id = installment.old_chapter_id
                  AND installment.old_chapter_id <> installment.installment_id;

                DELETE FROM book_chapter_details AS detail
                USING prismedia_legacy_comic_installments AS installment
                WHERE detail.entity_id = installment.installment_id;

                DELETE FROM book_details AS detail
                USING prismedia_legacy_comic_books AS legacy
                WHERE detail.entity_id = legacy.id;

                -- Remaining prose Books cannot carry the retired comic/manga vocabulary.
                UPDATE book_details
                SET book_type = 'book'
                WHERE book_type IN ('comic', 'manga');

                UPDATE book_acquisition_profiles
                SET allowed_formats = array_remove(allowed_formats, 'image-archive')
                WHERE allowed_formats @> ARRAY['image-archive']::text[];

                -- Both scanners gained new scan-owned projections in this release: Book now
                -- persists fixed-layout page counts, while Comic writes manifests and cached count
                -- rollups. Invalidate only their incremental snapshots so the first post-upgrade
                -- scan performs the required backfill even when source mtimes did not change.
                DELETE FROM scanned_files
                WHERE scan_kind IN ('scan-book', 'scan-comic');
                """);

            migrationBuilder.DropColumn(
                name: "cover_page_entity_id",
                table: "book_details");

            migrationBuilder.DropColumn(
                name: "cover_page_entity_id",
                table: "book_chapter_details");

            migrationBuilder.DeleteData(
                table: "entity_kinds",
                keyColumn: "code",
                keyValue: "book-page");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "page_count",
                table: "comic_installment_details");

            migrationBuilder.DropColumn(
                name: "page_count",
                table: "book_details");

            migrationBuilder.DropColumn(
                name: "page_count",
                table: "book_chapter_details");

            migrationBuilder.AddColumn<Guid>(
                name: "cover_page_entity_id",
                table: "book_details",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cover_page_entity_id",
                table: "book_chapter_details",
                type: "uuid",
                nullable: true);

            migrationBuilder.InsertData(
                table: "entity_kinds",
                columns: new[] { "code", "category", "display_name", "storage_shape" },
                values: new object[] { "book-page", "Media", "Book Page", "archive-entry" });
        }
    }
}
