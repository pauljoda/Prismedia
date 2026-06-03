#!/usr/bin/env bash
# Provision the throwaway jf-chair Jellyfin bench so it can answer PlaybackInfo
# for the same Sample Media library Prismedia serves. The bench config lives in
# /tmp (wiped on reboot), so this script is idempotent and safe to re-run.
#
#   ./provision-jellyfin.sh            # provision localhost:8098, admin/benchpass123
#
set -euo pipefail

JF="${JF:-http://localhost:8098}"
USER="${JF_USER:-admin}"
PASS="${JF_PASS:-benchpass123}"
MEDIA="${JF_MEDIA:-/media}"          # path inside the container (bind of Sample Media/Video)
AUTH='MediaBrowser Client="parity", Device="cli", DeviceId="parity-cli", Version="1.0"'

echo "==> Waiting for $JF to respond"
for _ in $(seq 1 30); do
  if curl -fsS "$JF/System/Info/Public" >/dev/null 2>&1; then break; fi
  sleep 2
done

wizard_done=$(curl -fsS "$JF/System/Info/Public" | jq -r '.StartupWizardCompleted')
if [ "$wizard_done" != "true" ]; then
  echo "==> Running startup wizard"
  curl -fsS -X POST "$JF/Startup/Configuration" -H 'Content-Type: application/json' \
    -d '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}' >/dev/null || true
  curl -fsS "$JF/Startup/User" >/dev/null || true
  curl -fsS -X POST "$JF/Startup/User" -H 'Content-Type: application/json' \
    -d "{\"Name\":\"$USER\",\"Password\":\"$PASS\"}" >/dev/null
  curl -fsS -X POST "$JF/Startup/RemoteAccess" -H 'Content-Type: application/json' \
    -d '{"EnableRemoteAccess":true,"EnableAutomaticPortMapping":false}' >/dev/null || true
  curl -fsS -X POST "$JF/Startup/Complete" >/dev/null
  echo "    wizard complete"
else
  echo "==> Startup wizard already complete"
fi

echo "==> Authenticating"
TOKEN=$(curl -fsS -X POST "$JF/Users/AuthenticateByName" \
  -H 'Content-Type: application/json' -H "X-Emby-Authorization: $AUTH" \
  -d "{\"Username\":\"$USER\",\"Pw\":\"$PASS\"}" | jq -r '.AccessToken')
echo "    token: ${TOKEN:0:8}..."

have_lib=$(curl -fsS "$JF/Library/VirtualFolders" -H "X-Emby-Token: $TOKEN" \
  | jq -r '[.[].Name] | index("Video")')
if [ "$have_lib" = "null" ]; then
  echo "==> Creating 'Video' library at $MEDIA"
  curl -fsS -X POST "$JF/Library/VirtualFolders?name=Video&collectionType=tvshows&refreshLibrary=true" \
    -H "X-Emby-Token: $TOKEN" -H 'Content-Type: application/json' \
    -d "{\"LibraryOptions\":{\"PathInfos\":[{\"Path\":\"$MEDIA\"}]}}" >/dev/null
else
  echo "==> 'Video' library already present; triggering refresh"
  curl -fsS -X POST "$JF/Library/Refresh" -H "X-Emby-Token: $TOKEN" >/dev/null || true
fi

echo "==> Waiting for scan to surface video items"
for _ in $(seq 1 60); do
  n=$(curl -fsS "$JF/Items?IncludeItemTypes=Movie,Episode,Video&Recursive=true&Limit=1" \
        -H "X-Emby-Token: $TOKEN" | jq -r '.TotalRecordCount')
  echo "    items so far: $n"
  [ "${n:-0}" -ge 1 ] && break
  sleep 3
done

echo "==> Done. TOKEN=$TOKEN"
