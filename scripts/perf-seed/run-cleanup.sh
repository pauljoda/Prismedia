#!/usr/bin/env bash
# Remove all perf-seeded duplicate videos and their copied thumbnail dirs.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CACHE_DIR="$REPO_ROOT/apps/backend/data/cache"
PG_CONTAINER="${PG_CONTAINER:-docker-postgres-1}"

echo "==> Deleting seeded rows in $PG_CONTAINER"
docker exec -i "$PG_CONTAINER" psql -U prismedia -d prismedia < "$REPO_ROOT/scripts/perf-seed/cleanup.sql"

echo "==> Removing copied thumbnail dirs"
docker exec "$PG_CONTAINER" cat /tmp/perf_seed_cleanup_ids.txt > /tmp/perf_seed_cleanup_ids.txt || true
removed=0
while IFS= read -r id; do
  id="$(echo "$id" | tr -d '[:space:]')"
  [ -z "$id" ] && continue
  dir="$CACHE_DIR/videos/$id"
  if [ -d "$dir" ]; then
    rm -rf "$dir"
    removed=$((removed+1))
  fi
  rm -f "$CACHE_DIR/grid-thumbs/$id.jpg"
done < /tmp/perf_seed_cleanup_ids.txt

echo "==> Removed $removed thumbnail dirs. Library restored to originals."
