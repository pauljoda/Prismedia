---
sidebar_position: 2
title: Troubleshooting
description: Common issues, where to look, and how to recover.
---

# Troubleshooting

Things that go wrong, why, and how to fix them. If you hit something not on this list, the [Jobs & Operations](../using/jobs.md) page and `docker compose logs prismedia` are the right first stops.

## The container won't start

```bash
docker compose logs prismedia --tail 200
```

Common causes:

- **Postgres failed to start** — usually a permissions issue on `/data/postgres`. Stop the container, fix ownership, restart.
- **Migrations failed** — the migrator refuses to start the app on an inconsistent schema. The error is verbatim in the logs. If you see `relation "..." does not exist` after a destructive change, read the release notes and restore your last `/data` snapshot if you need to roll back. See [Upgrading & Rollback](../deployment/upgrading.md).
- **Port 8008 already in use** — another container or process is bound. Find it with `lsof -i :8008` (host) or `docker ps`.

## Library scan finds nothing

- **The library root path is the path inside the container**, not on the host. If you mounted `/srv/movies:/media/movies`, the root path is `/media/movies`.
- **Scan-type flags** must include the media type you're scanning. A root with Videos off won't pick up videos.
- **Recursive** must be on if your files are in subdirectories.
- **Files must match supported extensions.** See the per-type lists in [Library & Scanning](../library/overview.md) — for example video is `.mp4 .m4v .mkv .mov .webm .avi .wmv .flv .ts .m2ts .mpg .mpeg`.
- **Generated artifacts are skipped.** Filenames ending in `-preview`/`-sample`/`-thumb`/`-sprite` (with `-`/`_`/`.` separators) are excluded by design, and hidden (dot-prefixed) folders are never scanned.

Trigger a scan from **Jobs → Library scan → Run** and watch the live count.

## Videos become movies/episodes when they shouldn't

The video classifier is **folder-based**, not filename-based. Re-read [Videos, Movies & Series](../library/videos.md):

- A **single** video in a folder directly under the root is a **movie**.
- A folder of **two or more** videos becomes a **series** (each an episode).
- Use `Series/Season NN/SxxEyy.ext` for unambiguous TV results.

After fixing the layout, rescan — classification is idempotent on file paths.

## Identify returns nothing

- **The right provider is selected.** TMDB won't find adult content; a Stash scraper for one site won't find another's.
- **NSFW mode** filters the provider picker. Reveal NSFW content to use NSFW providers. See [Stash Compatibility](./stash-compatibility.md).
- **API keys are set.** Open **Plugins → Installed**, expand the plugin, and ensure the auth field has a value.
- **Title parsing.** Ugly filenames can confuse name-based providers; the identify query is cleaned (parentheses/brackets stripped, accents flattened), but you can edit the search text yourself.
- **Network access from the container.** Plugins make HTTP calls; check logs for `ECONNREFUSED`/`ETIMEDOUT` if the container has no egress.

## HLS / transcoded playback stalls

Most files now **direct-play** or **stream-copy**; a full transcode only happens when the browser can't decode the source.

- **Try Direct mode** in the player's quality menu. If Direct works, the issue is in transcoding.
- **Check Jobs → preview/HLS queues** for a failed job and its error.
- **ffmpeg errors:** `docker compose logs prismedia | grep ffmpeg`.
- **Hardware transcode:** set `PRISMEDIA_HLS_TRANSCODER` / `PRISMEDIA_VAAPI_DEVICE` if the auto profile picks the wrong encoder. See [HLS Streaming](../developers/hls-streaming.md).

## Trickplay (timeline hover) doesn't show thumbnails

- Generation must have completed — check the **preview** job for the video in Jobs.
- Rebuild from **Settings → Generated Storage**, or queue it again from Jobs.

## Subtitles aren't auto-loading

- **Auto-enable** must be on in **Settings → Subtitles**.
- **Preferred languages** must include the track's language code.
- The track must exist — embedded subtitles are extracted by the subtitles job; confirm it succeeded in Jobs.

For a per-video override, use the player's subtitle button.

## Jellyfin client can't connect or sign in

- Use the LAN **IP and port 8008**, not `localhost`, from other devices.
- Sign in with a **Jellyfin profile** username and the **API key** as the password (Settings → API Access). There are no per-user passwords.
- If everything signed out at once, the API key was **regenerated** (which invalidates sessions) — sign in again.
- Behind a reverse proxy/SSO, the Jellyfin routes must **bypass** the proxy auth. See [Reverse Proxy & Auth Middleware](../deployment/reverse-proxy.md).
- More client tips: [Connecting Infuse & Manet](../jellyfin/clients.md).

## Plugin credentials lost after an update

Plugin credentials are encrypted with `PRISMEDIA_SECRET`, which the container auto-generates and persists to `/data/.prismedia-secret`. Credentials survive container recreation **as long as `/data` persists**. They only become unreadable if that secret is lost — for example a wiped `/data`, or changing an explicitly-set `PRISMEDIA_SECRET`. Back up `/data` and, if you set `PRISMEDIA_SECRET` yourself, keep it stable. See [Authentication & API Keys](../deployment/authentication.md).

## Jobs are stuck "running" forever

- The worker may have crashed or been killed mid-job. The container restarts it automatically; on the next boot, stale running jobs are recovered after their lease expires.
- If a job has been "running" for hours while the queue is idle, it's orphaned — cancel it from the Jobs page.

## "Failures" count keeps growing

Open the **Failures** section in Jobs and read the actual errors:

- **One bad file** repeatedly failing a probe → fix or delete the file.
- **Disk full** under `/data` → free space, then retry.
- **External service down** (provider API) → retry once it's back.

After resolution, **Clear** the rows. They stay in `job_runs` for audit but stop showing in the count.

## How to file a useful bug report

Include:

- **Prismedia version** — Settings → Diagnostics or GitHub Releases.
- **Container logs around the failure** — `docker compose logs prismedia --tail 500 > prismedia.log`.
- **What you did** — exact steps from a known good state.
- **What you expected vs what happened.**
- **Whether `/data` is on a host bind, a named volume, or a network mount** — disk semantics affect a surprising number of bugs.

Open issues at [github.com/pauljoda/Prismedia/issues](https://github.com/pauljoda/Prismedia/issues).

## Useful queries

```sql
-- Recent failures
SELECT type, target_label, message, finished_at
FROM job_runs
WHERE status = 'failed'
ORDER BY finished_at DESC LIMIT 20;

-- Longest-running jobs
SELECT type, target_label, finished_at - started_at AS duration
FROM job_runs
WHERE finished_at IS NOT NULL
ORDER BY duration DESC LIMIT 20;
```
