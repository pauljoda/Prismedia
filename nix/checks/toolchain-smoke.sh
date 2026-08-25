#!/usr/bin/env bash

set -euo pipefail

readonly expected_node_major="22"
readonly expected_pnpm_version="10.30.3"
readonly expected_dotnet_major="10"
readonly expected_playwright_version="1.60.0"

fail() {
  echo "toolchain smoke check: $*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "missing command: $1"
}

require_version_prefix() {
  local command_name="$1"
  local expected_prefix="$2"
  local actual_version

  actual_version="$($command_name --version | head -n 1)"
  [[ "$actual_version" == "$expected_prefix"* ]] ||
    fail "$command_name reported '$actual_version'; expected prefix '$expected_prefix'"
}

for command_name in \
  curl docker dotnet ffmpeg ffprobe git jq node pg_dump pg_restore pnpm python3 shellcheck unzip; do
  require_command "$command_name"
done

require_version_prefix node "v${expected_node_major}."
require_version_prefix dotnet "${expected_dotnet_major}."

actual_pnpm_version="$(pnpm --version)"
[[ "$actual_pnpm_version" == "$expected_pnpm_version" ]] ||
  fail "pnpm reported '$actual_pnpm_version'; expected '$expected_pnpm_version'"

[[ "${PRISMEDIA_PLAYWRIGHT_VERSION:-}" == "$expected_playwright_version" ]] ||
  fail "PRISMEDIA_PLAYWRIGHT_VERSION must be '$expected_playwright_version'"

[[ -d "${PLAYWRIGHT_BROWSERS_PATH:-}" ]] ||
  fail "PLAYWRIGHT_BROWSERS_PATH does not identify a browser directory"

compgen -G "${PLAYWRIGHT_BROWSERS_PATH}/chromium-*" >/dev/null ||
  fail "the Playwright Chromium browser is missing"

compgen -G "${PLAYWRIGHT_BROWSERS_PATH}/chromium_headless_shell-*" >/dev/null ||
  fail "the Playwright Chromium headless shell is missing"

[[ -x "${PRISMEDIA_FFMPEG_PATH:-}" ]] || fail "PRISMEDIA_FFMPEG_PATH is not executable"
[[ -x "${PRISMEDIA_FFPROBE_PATH:-}" ]] || fail "PRISMEDIA_FFPROBE_PATH is not executable"
[[ -x "${PRISMEDIA_PG_DUMP_PATH:-}" ]] || fail "PRISMEDIA_PG_DUMP_PATH is not executable"
[[ -x "${PRISMEDIA_PG_RESTORE_PATH:-}" ]] || fail "PRISMEDIA_PG_RESTORE_PATH is not executable"
[[ "${CreateSymbolicLinksForCopyFilesToOutputDirectoryIfPossible:-}" == "true" ]] ||
  fail "MSBuild output symlinks must be enabled for Nix store reference assemblies"

ffmpeg_version="$(ffmpeg -hide_banner -version | head -n 1)"
[[ "$ffmpeg_version" == *"-Jellyfin"* ]] ||
  fail "ffmpeg is not the Jellyfin build: $ffmpeg_version"
[[ -n "${PRISMEDIA_JELLYFIN_FFMPEG_VERSION:-}" ]] ||
  fail "PRISMEDIA_JELLYFIN_FFMPEG_VERSION is not set"

python3 - <<'PY'
import bs4
import cloudscraper
import dateutil
import lxml
import requests
import stashapi
PY

docker compose version >/dev/null
docker buildx version >/dev/null

if [[ "$(uname -s)" == "Linux" ]]; then
  ffmpeg_encoders="$(ffmpeg -hide_banner -encoders 2>/dev/null)"
  for encoder_name in h264_nvenc h264_qsv h264_vaapi; do
    grep -q "[[:space:]]${encoder_name}[[:space:]]" <<<"$ffmpeg_encoders" ||
      fail "ffmpeg is missing the $encoder_name encoder"
  done
fi

echo "Prismedia Nix toolchain smoke check passed."
