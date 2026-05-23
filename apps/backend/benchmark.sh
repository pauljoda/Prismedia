#!/usr/bin/env bash
#
#  Benchmark Runner
# Clears generated assets, triggers a library scan, monitors job completion,
# and captures all [METRICS] timing output.
#
set -euo pipefail

DB="postgresql://prismedia:prismedia@localhost:5432/prismedia"
CACHE_DIR="/tmp/prismedia-benchmark-cache"
DATA_DIR="/tmp/prismedia-benchmark-data"

mkdir -p "$CACHE_DIR" "$DATA_DIR"

echo "===  BENCHMARK: Preparing ==="

# Clear generated data so we do a full re-generate
docker exec docker-postgres-1 psql -U prismedia -d prismedia -c "
-- Remove all generated (non-source) entity files
DELETE FROM entity_files WHERE role != 'source';
-- Clear technical metadata so probe re-runs
DELETE FROM entity_technical;
-- Clear fingerprints so fingerprint jobs re-run
DELETE FROM entity_file_fingerprints;
-- Clear subtitle extraction timestamps
UPDATE video_details SET subtitles_extracted_at = NULL;
-- Remove pending/queued jobs
DELETE FROM job_runs WHERE status IN ('queued', 'running');
"

echo "===  BENCHMARK: Starting worker + scan ==="

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"
LOG_FILE="/tmp/prismedia-benchmark-output.log"

# Build first
dotnet build apps/backend/src/Prismedia.Worker/Prismedia.Worker.csproj -c Release -q

# Start worker in background, capture output to log file
DATABASE_URL="$DB" \
PRISMEDIA_CACHE_DIR="$CACHE_DIR" \
PRISMEDIA_DATA_DIR="$DATA_DIR" \
dotnet run --project apps/backend/src/Prismedia.Worker/Prismedia.Worker.csproj -c Release --no-build 2>&1 | tee "$LOG_FILE" &
WORKER_PID=$!
echo "Worker PID: $WORKER_PID"

sleep 3  # Let worker start up

# Enqueue a library scan
docker exec docker-postgres-1 psql -U prismedia -d prismedia -c "
INSERT INTO job_runs (id, type, status, payload_json, priority, attempts, max_attempts, progress, available_at, created_at)
VALUES (gen_random_uuid(), 'scan-library', 'queued', '{}', 50, 0, 3, 0, now(), now());
"

echo "===  BENCHMARK: Monitoring ==="
START_TIME=$(date +%s)

IDLE_COUNT=0
while true; do
    sleep 5

    COUNTS=$(docker exec docker-postgres-1 psql -U prismedia -d prismedia -t -c "
        SELECT
            count(*) FILTER (WHERE status IN ('queued', 'running')) as pending,
            count(*) FILTER (WHERE status = 'completed') as completed,
            count(*) FILTER (WHERE status = 'failed') as failed
        FROM job_runs
        WHERE created_at > now() - interval '30 minutes';
    " 2>/dev/null | tr -d ' ')

    PENDING=$(echo "$COUNTS" | cut -d'|' -f1)
    COMPLETED=$(echo "$COUNTS" | cut -d'|' -f2)
    FAILED=$(echo "$COUNTS" | cut -d'|' -f3)

    echo "[-POLL] pending=$PENDING completed=$COMPLETED failed=$FAILED"

    if [ "$PENDING" = "0" ] || [ -z "$PENDING" ]; then
        IDLE_COUNT=$((IDLE_COUNT + 1))
        if [ $IDLE_COUNT -ge 3 ]; then
            END_TIME=$(date +%s)
            TOTAL=$((END_TIME - START_TIME))
            echo ""
            echo "===  BENCHMARK COMPLETE ==="
            echo "Total wall time: ${TOTAL}s"
            echo "Jobs completed: $COMPLETED"
            echo "Jobs failed: $FAILED"
            kill $WORKER_PID 2>/dev/null || true
            exit 0
        fi
    else
        IDLE_COUNT=0
    fi
done
