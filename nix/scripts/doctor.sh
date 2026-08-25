# shellcheck shell=bash

set -euo pipefail

skip_docker=false

if [[ "${1:-}" == "--skip-docker" ]]; then
  skip_docker=true
  shift
fi

if (( $# > 0 )); then
  echo "usage: prismedia-doctor [--skip-docker]" >&2
  exit 64
fi

failures=0

pass() {
  printf 'ok    %s\n' "$1"
}

fail() {
  printf 'error %s\n' "$1" >&2
  failures=$((failures + 1))
}

require_command() {
  if command -v "$1" >/dev/null 2>&1; then
    pass "$1 is available"
    return
  fi

  fail "$1 is missing"
}

for command_name in docker dotnet ffmpeg ffprobe git node pg_dump pg_restore pnpm python3; do
  require_command "$command_name"
done

node_version="$(node --version)"
if [[ "$node_version" == "v${PRISMEDIA_NODE_MAJOR}."* ]]; then
  pass "Node.js is $node_version"
else
  fail "Node.js is $node_version; expected major ${PRISMEDIA_NODE_MAJOR}"
fi

if [[ -f package.json ]]; then
  expected_pnpm_version="$(node -p "require('./package.json').packageManager.split('@').at(-1)")"
  actual_pnpm_version="$(pnpm --version)"
  if [[ "$actual_pnpm_version" == "$expected_pnpm_version" ]]; then
    pass "pnpm is $actual_pnpm_version"
  else
    fail "pnpm is $actual_pnpm_version; package.json requires $expected_pnpm_version"
  fi
else
  fail "run the doctor from the Prismedia repository root"
fi

dotnet_version="$(dotnet --version)"
if [[ "$dotnet_version" == 10.* ]]; then
  pass ".NET SDK is $dotnet_version"
else
  fail ".NET SDK is $dotnet_version; expected .NET 10"
fi

ffmpeg_version="$("$PRISMEDIA_FFMPEG_PATH" -hide_banner -version | head -n 1)"
if [[ "$ffmpeg_version" == *"-Jellyfin"* ]]; then
  pass "Jellyfin FFmpeg $PRISMEDIA_JELLYFIN_FFMPEG_VERSION is available"
else
  fail "ffmpeg is not the Jellyfin build: $ffmpeg_version"
fi

if [[ -d "$PLAYWRIGHT_BROWSERS_PATH" ]]; then
  pass "Playwright $PRISMEDIA_PLAYWRIGHT_VERSION Chromium is installed"
else
  fail "Playwright browser directory is missing"
fi

if python3 -c 'import bs4, cloudscraper, dateutil, lxml, requests, stashapi' 2>/dev/null; then
  pass "Python scraper dependencies import successfully"
else
  fail "Python scraper dependencies could not be imported"
fi

if $skip_docker; then
  pass "Docker daemon check skipped"
elif docker info >/dev/null 2>&1; then
  pass "Docker daemon is reachable"
else
  fail "Docker daemon is not reachable; start Docker or configure DOCKER_HOST"
fi

if (( failures > 0 )); then
  printf '\nPrismedia doctor found %d problem(s).\n' "$failures" >&2
  exit 1
fi

echo
echo "Prismedia development environment is ready."
