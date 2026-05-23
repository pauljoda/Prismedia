#!/bin/sh
set -e

PGDATA="${PGDATA:-/data/postgres}"
CACHE_DIR="/data/cache"
SECRET_FILE="/data/.prismedia-secret"

# ── Ensure directories exist ──────────────────────────────────────
mkdir -p "$PGDATA" "$CACHE_DIR" /run/postgresql
chown -R postgres:postgres "$PGDATA" /run/postgresql

# ── Resolve or generate PRISMEDIA_SECRET ────────────────────────────
# Used by the API to encrypt plugin credentials (e.g. TMDB API keys) at rest.
# Prefer an explicit env var; otherwise persist a randomly generated secret in
# the data volume so plugin auth survives container recreation without making
# users provision one by hand.
if [ -z "$PRISMEDIA_SECRET" ]; then
  if [ -f "$SECRET_FILE" ]; then
    PRISMEDIA_SECRET="$(cat "$SECRET_FILE")"
  else
    echo "[prismedia] Generating new PRISMEDIA_SECRET for plugin credential encryption..."
    PRISMEDIA_SECRET="$(head -c 48 /dev/urandom | base64 | tr -d '\n/+=' | head -c 48)"
    umask 077
    printf '%s' "$PRISMEDIA_SECRET" > "$SECRET_FILE"
    chmod 600 "$SECRET_FILE"
  fi
fi
export PRISMEDIA_SECRET

# ── Initialize PostgreSQL if fresh ────────────────────────────────
if [ ! -f "$PGDATA/PG_VERSION" ]; then
  echo "[prismedia] Initializing PostgreSQL database..."
  gosu postgres initdb -D "$PGDATA" --auth=trust --encoding=UTF8

  # Configure for local-only access
  cat > "$PGDATA/pg_hba.conf" <<CONF
local   all   all                 trust
host    all   all   127.0.0.1/32  trust
host    all   all   ::1/128       trust
CONF

  # Tune for embedded single-user usage. max_connections leaves enough room
  # for the .NET API, worker, and EF migration/runtime pools.
  cat >> "$PGDATA/postgresql.conf" <<CONF
listen_addresses = '127.0.0.1'
unix_socket_directories = '/run/postgresql'
shared_buffers = 128MB
work_mem = 4MB
max_connections = 40
logging_collector = off
log_destination = 'stderr'
CONF
fi

# ── Start PostgreSQL ──────────────────────────────────────────────
echo "[prismedia] Starting PostgreSQL..."
gosu postgres pg_ctl -D "$PGDATA" -l /data/postgres/log -w -t 30 start

# Create database if it doesn't exist
gosu postgres psql -h 127.0.0.1 -tc "SELECT 1 FROM pg_database WHERE datname = 'prismedia'" | grep -q 1 || \
  gosu postgres createdb -h 127.0.0.1 prismedia

# Note: database migrations run automatically in the shared .NET runtime
# used by the API and worker.

# ── Start worker ──────────────────────────────────────────────────
echo "[prismedia] Starting background worker..."
DATABASE_URL="postgresql://postgres@127.0.0.1:5432/prismedia" \
PRISMEDIA_CACHE_DIR="$CACHE_DIR" \
PRISMEDIA_DATA_DIR="/data" \
PRISMEDIA_SECRET="$PRISMEDIA_SECRET" \
  dotnet /app/worker/Prismedia.Worker.dll &

# ── Start .NET API (foreground — keeps container alive) ───────────
echo "[prismedia] Starting .NET API and web frontend on port 8008..."
echo "[prismedia] Ready — http://localhost:8008"
exec env \
  DATABASE_URL="postgresql://postgres@127.0.0.1:5432/prismedia" \
  PRISMEDIA_CACHE_DIR="$CACHE_DIR" \
  PRISMEDIA_DATA_DIR="/data" \
  PRISMEDIA_SECRET="$PRISMEDIA_SECRET" \
  PRISMEDIA_STATIC_WEB_ROOT="${PRISMEDIA_STATIC_WEB_ROOT:-/app/wwwroot}" \
  PUBLIC_APP_URL="http://localhost:8008" \
  PUBLIC_API_URL="/api" \
  ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:8008}" \
  dotnet /app/api/Prismedia.Api.dll
