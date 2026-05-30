#!/usr/bin/env bash
# Seed duplicate videos into the dev DB and copy a distinct thumbnail file for
# each so the EntityGrid can be stress-tested. Safe to re-run (adds another batch).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CACHE_DIR="$REPO_ROOT/apps/backend/data/cache"
PG_CONTAINER="${PG_CONTAINER:-docker-postgres-1}"

echo "==> Running seed SQL in $PG_CONTAINER"
docker exec -i "$PG_CONTAINER" psql -U prismedia -d prismedia < "$REPO_ROOT/scripts/perf-seed/seed.sql"

echo "==> Exporting manifest from container"
docker exec "$PG_CONTAINER" cat /tmp/perf_seed_manifest.tsv > /tmp/perf_seed_manifest.tsv

copied=0
missing=0
while IFS=$'\t' read -r new_id cover_path; do
  [ -z "$new_id" ] && continue
  src="$CACHE_DIR/${cover_path#/assets/}"
  dest_dir="$CACHE_DIR/videos/$new_id"
  if [ ! -f "$src" ]; then
    missing=$((missing+1))
    continue
  fi
  mkdir -p "$dest_dir"
  cp "$src" "$dest_dir/thumb.jpg"
  copied=$((copied+1))
done < /tmp/perf_seed_manifest.tsv

echo "==> Copied $copied thumbnails ($missing source files missing)"
echo "==> Done. Reload the Videos page to see the inflated grid."
