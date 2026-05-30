#!/usr/bin/env bash
# Dev backfill: generate the small grid-card cover variant (/assets/grid-thumbs/<id>.jpg)
# for every video that has a cover but no grid thumbnail, then record GridThumbnail rows.
# Mirrors what GridThumbnailService does in production; uses macOS `sips` so it can run
# without the worker. Safe to re-run.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CACHE_DIR="$REPO_ROOT/apps/backend/data/cache"
PG_CONTAINER="${PG_CONTAINER:-docker-postgres-1}"
MAX=480

# Resolve each video's winning cover (same priority as the backend) that lacks a grid thumb.
docker exec "$PG_CONTAINER" psql -U prismedia -d prismedia -At -F $'\t' -c "
  SELECT e.id,
    (SELECT ef.path FROM entity_files ef
       WHERE ef.entity_id = e.id
         AND ef.role IN ('thumbnail','poster','cover','logo','backdrop')
       ORDER BY (CASE WHEN ef.source='custom' OR ef.path ILIKE '%/custom/artwork/%' OR ef.path ILIKE '%/plugins/artwork/%' THEN 0 ELSE 1 END),
                (CASE ef.role WHEN 'thumbnail' THEN 0 WHEN 'poster' THEN 1 WHEN 'cover' THEN 2 WHEN 'logo' THEN 3 ELSE 4 END),
                ef.created_at
       LIMIT 1)
  FROM entities e
  WHERE e.kind_code='video' AND e.deleted_at IS NULL
    AND NOT EXISTS (SELECT 1 FROM entity_files g WHERE g.entity_id=e.id AND g.role='grid-thumbnail')
" > /tmp/grid_backfill.tsv

mkdir -p "$CACHE_DIR/grid-thumbs"
: > /tmp/grid_backfill_rows.sql
made=0
while IFS=$'\t' read -r id cover; do
  [ -z "$id" ] || [ -z "$cover" ] && continue
  src="$CACHE_DIR/${cover#/assets/}"
  [ -f "$src" ] || continue
  out="$CACHE_DIR/grid-thumbs/$id.jpg"
  sips -Z "$MAX" -s format jpeg "$src" --out "$out" >/dev/null 2>&1 || continue
  size=$(stat -f%z "$out")
  printf "INSERT INTO entity_files (id, entity_id, role, path, mime_type, size_bytes, source, created_at, updated_at) VALUES (gen_random_uuid(), '%s', 'grid-thumbnail', '/assets/grid-thumbs/%s.jpg', 'image/jpeg', %s, 'scan', now(), now());\n" "$id" "$id" "$size" >> /tmp/grid_backfill_rows.sql
  made=$((made+1))
done < /tmp/grid_backfill.tsv

if [ "$made" -gt 0 ]; then
  docker exec -i "$PG_CONTAINER" psql -U prismedia -d prismedia -q < /tmp/grid_backfill_rows.sql >/dev/null
fi
echo "==> Generated $made grid thumbnails."
