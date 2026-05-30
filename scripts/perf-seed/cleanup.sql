-- Remove all perf-seeded duplicate videos. Deleting the entities rows cascades
-- to video_details / entity_technical / entity_files / etc. The disk thumbnail
-- dirs are removed by run-cleanup.sh using the exported id list.
\set ON_ERROR_STOP on

\copy (SELECT id FROM entities WHERE kind_code='video' AND title LIKE '% ⟮perf#%⟯%') TO '/tmp/perf_seed_cleanup_ids.txt'

DELETE FROM entities WHERE kind_code='video' AND title LIKE '% ⟮perf#%⟯%';

SELECT count(*) AS remaining_videos FROM entities WHERE kind_code='video' AND deleted_at IS NULL;
