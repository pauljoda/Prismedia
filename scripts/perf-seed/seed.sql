-- Perf-test seed: inflate the video library by cloning the existing top-level
-- videos so the EntityGrid can be stress-tested with a realistic item count.
--
-- Each duplicate gets a NEW entity id, its own entities/video_details/
-- entity_technical/entity_files rows, and a distinct cover URL
-- (/assets/videos/<new-id>/thumb.jpg). The physical thumbnail file is copied on
-- disk by run-seed.sh using the manifest exported at the end of this script.
--
-- Duplicates are tagged with a ' ⟮perf#N⟯' title suffix so cleanup.sql can find and
-- remove them. Deleting the entities rows cascades to all cloned child rows.

\set ON_ERROR_STOP on

-- How many duplicates to create. 12 originals + 238 = 250 total videos.
\set dup_count 238

BEGIN;

CREATE TEMP TABLE perf_seed_map ON COMMIT DROP AS
WITH srcs AS (
  SELECT
    e.id            AS src_id,
    e.kind_code,
    e.sort_order,
    e.rating_value,
    e.is_favorite,
    e.is_nsfw,
    e.is_organized,
    e.title         AS src_title,
    (SELECT ef.path
       FROM entity_files ef
      WHERE ef.entity_id = e.id
        AND ef.role IN ('thumbnail','poster','cover','logo','backdrop')
      ORDER BY
        (CASE WHEN ef.source = 'custom'
               OR ef.path ILIKE '%/custom/artwork/%'
               OR ef.path ILIKE '%/plugins/artwork/%' THEN 0 ELSE 1 END),
        (CASE ef.role WHEN 'thumbnail' THEN 0 WHEN 'poster' THEN 1
               WHEN 'cover' THEN 2 WHEN 'logo' THEN 3 ELSE 4 END),
        ef.created_at
      LIMIT 1)      AS cover_path,
    row_number() OVER (ORDER BY e.id) AS rn
  FROM entities e
  WHERE e.kind_code = 'video'
    AND e.parent_entity_id IS NULL
    AND e.deleted_at IS NULL
),
src_count AS (SELECT count(*)::int AS c FROM srcs),
nums AS (SELECT generate_series(1, :dup_count) AS n)
SELECT
  gen_random_uuid() AS new_id,
  s.src_id, s.kind_code, s.sort_order, s.rating_value,
  s.is_favorite, s.is_nsfw, s.is_organized, s.src_title, s.cover_path,
  nums.n
FROM nums
JOIN src_count sc ON true
JOIN srcs s ON s.rn = ((nums.n - 1) % sc.c) + 1;

-- Parent entity rows. created_at is spread backwards in time so the default
-- "added desc" sort interleaves duplicates rather than clustering them.
INSERT INTO entities (id, kind_code, title, parent_entity_id, sort_order,
                      created_at, updated_at, deleted_at,
                      is_favorite, is_nsfw, is_organized, rating_value)
SELECT new_id, kind_code,
       src_title || ' ⟮perf#' || n || '⟯',
       NULL, sort_order,
       now() - (n || ' minutes')::interval, now(), NULL,
       is_favorite, is_nsfw, is_organized, rating_value
FROM perf_seed_map;

INSERT INTO video_details (entity_id, library_root_id, subtitles_extracted_at)
SELECT m.new_id, vd.library_root_id, vd.subtitles_extracted_at
FROM perf_seed_map m
JOIN video_details vd ON vd.entity_id = m.src_id;

INSERT INTO entity_technical (entity_id, duration_seconds, width, height, frame_rate,
                             bit_rate, sample_rate, channels, codec, container, format, updated_at)
SELECT m.new_id, t.duration_seconds, t.width, t.height, t.frame_rate, t.bit_rate,
       t.sample_rate, t.channels, t.codec, t.container, t.format, now()
FROM perf_seed_map m
JOIN entity_technical t ON t.entity_id = m.src_id;

-- Distinct cover per duplicate (file copied on disk by run-seed.sh).
INSERT INTO entity_files (id, entity_id, role, path, mime_type, size_bytes, source, created_at, updated_at)
SELECT gen_random_uuid(), m.new_id, 'thumbnail',
       '/assets/videos/' || m.new_id || '/thumb.jpg',
       'image/jpeg', NULL, 'scan', now(), now()
FROM perf_seed_map m;

-- Trickplay hover reuses the source m3u8 so hover-scrub behaviour stays testable.
INSERT INTO entity_files (id, entity_id, role, path, mime_type, size_bytes, source, created_at, updated_at)
SELECT gen_random_uuid(), m.new_id, 'trickplay', tf.path, tf.mime_type, tf.size_bytes, tf.source, now(), now()
FROM perf_seed_map m
JOIN entity_files tf ON tf.entity_id = m.src_id AND tf.role = 'trickplay';

\copy (SELECT new_id, cover_path FROM perf_seed_map) TO '/tmp/perf_seed_manifest.tsv'

COMMIT;

SELECT count(*) AS total_videos FROM entities WHERE kind_code = 'video' AND deleted_at IS NULL;
