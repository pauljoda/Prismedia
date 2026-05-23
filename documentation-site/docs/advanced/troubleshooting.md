---
sidebar_position: 3
title: Troubleshooting
description: Common issues, where to look, and how to recover.
---

# Troubleshooting

A list of things that go wrong, why they go wrong, and how to fix them. If you hit something not on this list, the **Operations** page and `docker compose logs prismedia` are the right first stops.

## The container won't start

Check the logs:

```bash
docker compose logs prismedia --tail 200
```

Common causes:

- **Postgres failed to start** — usually a permissions issue on `/data/postgres`. Stop the container, fix ownership, restart:
  ```bash
  docker compose down
  sudo chown -R 999:999 /var/lib/docker/volumes/<volume>/_data/postgres   # path varies
  docker compose up -d
  ```
- **Migrations failed** — the migrator refuses to start the app on an inconsistent schema. The error is verbatim in the logs. If you see `relation "..." does not exist` after a destructive change, read the release notes and restore your last `/data` snapshot if you need to roll back.
- **Port 8008 already in use** — another container or process is bound. Find it: `lsof -i :8008` (host) or `docker ps` to spot the conflict.

## A migration fails after upgrading

Prismedia applies EF Core migrations on startup. If a migration fails:

- Stop the container.
- Read the failing SQL/error in `docker compose logs prismedia --tail 200`.
- Check `CHANGELOG.md` for any required rescan or manual action.
- Restore `/data` from your pre-upgrade snapshot if you need to go back to the previous image.

## Library scan finds nothing

- **The library root path is the path inside the container**, not on the host. If you mounted `/srv/movies:/media/movies`, the library root path is `/media/movies`.
- **Scan-type flags** must include the media type you're scanning. A root with `scan_videos = false` won't pick up videos no matter how many files are there.
- **Recursive** must be on if your files are in subdirectories.
- **Files must match supported extensions.** `supportedVideoExtensions` includes `.mp4`, `.mkv`, `.mov`, `.webm`, `.avi`, `.wmv`, `.flv`, `.ts`, `.m2ts`, `.mpg`, `.mpeg`. Files in other formats are ignored.
- **Generated artifacts are skipped.** Filenames containing `-preview`, `-sample`, `-trailer`, `_preview`, `_sample`, `_trailer` are excluded by design.

Manually trigger a scan from **Operations** → **Library scan** → **Run** and watch the live count.

## Video files become movies when they should be episodes

The classifier looks at **folder depth**, not filenames. Re-read [Library Organization](../users/library-organization.md) for the depth rules.

If your layout is:

```text
/library/Movies/Heat (1995).mkv          ← depth 0 → movie  ✓
/library/Movies/My Show/S01E01.mkv       ← depth 1 → flat episode ✗ (you wanted seasoned series)
```

You need to either:

- Move `My Show` out of `/library/Movies` into its own series root, or
- Add an intermediate `Season 1` folder so depth becomes 2 → seasoned episode.

After fixing the layout, re-run the library scan. The classifier is idempotent on file paths, so re-scanning is safe.

## Identify returns nothing

A few things to check:

- **The right provider is selected.** TMDB won't find adult content; a Stash community scraper for one site won't find content from another.
- **NSFW mode** filters the provider picker. Switch to **Show** if you're trying to use an NSFW provider.
- **API keys are set.** Open **Plugins → Installed**, expand the plugin, ensure the auth field has a value. Click **Test** if the plugin supports it.
- **Title parsing.** For name-based providers, the title field is what matches. Ugly filenames (`Movie.2019.1080p.BluRay.x264.mkv`) sometimes confuse the parser. Edit the title manually before identify.
- **Network access from the container.** Plugins make HTTP calls; if the container is on an isolated network without egress, identify will fail. Check `docker compose logs prismedia` for `ECONNREFUSED` or `ETIMEDOUT`.

For StashBox specifically, see [StashBox Endpoints](./stashbox.md).

## HLS playback stalls or shows "loading" forever

HLS is generated lazily on first request. The first manifest request enqueues a `preview` job; segments stream as they're written.

- **Check Operations → Video media pipeline → preview queue.** If the job is failed, click in for the error.
- **Check the cache.** `docker compose exec prismedia ls -la /data/cache/hls/<videoId>/` should show `master.m3u8` and growing `.ts` segments.
- **Try Direct mode.** In the player's quality menu pick **Direct** — bypasses HLS entirely. If Direct works, the problem is in transcoding.
- **ffmpeg errors.** `docker compose logs prismedia | grep ffmpeg` for the verbatim invocation and any error output.

To force a rebuild:

```bash
docker compose exec prismedia rm -rf /data/cache/hls/<videoId>
```

The next playback request will re-trigger generation.

## Trickplay (timeline hover) doesn't show thumbnails

- **Generation must have completed.** Check `library_settings.generate_trickplay = true` and that the `preview` job for this video succeeded.
- **The cache files must exist.** `ls /data/cache/hls/<videoId>/*_thumbnails.{vtt,jpg}`.
- **The browser fetched them.** Open dev tools → Network → filter for `_thumbnails` while hovering the timeline.

If files are missing, **Settings → Generated Storage → Force-rebuild previews** for that video, or queue manually via Operations.

## Subtitles aren't auto-loading

- **Auto-enable** must be on in **Settings → Subtitles**.
- **Preferred languages** must include the language code on the subtitle track (`en`, `eng`, etc.). Track languages are stored in `video_subtitles.language` and come from the source file's metadata.
- The track must exist. Embedded subtitles are extracted by the `extract-subtitles` job — confirm in Operations → Video media pipeline → extract-subtitles.

For per-video override, use the player's subtitle button.

## Plugin auth gets wiped after a container update

Plugin and StashBox credentials are encrypted with `PRISMEDIA_SECRET`. If `PRISMEDIA_SECRET` changes (or is unset, which generates a new random key per restart), existing encrypted values become unreadable and you'll re-enter them.

Fix: set `PRISMEDIA_SECRET` to a long random string in your compose env, persistently:

```yaml
services:
  prismedia:
    environment:
      PRISMEDIA_SECRET: <a-long-random-string-keep-this-stable>
```

## Jobs are stuck "running" forever

- The worker may have crashed or been killed mid-job. Restart the container; on next boot, stuck jobs are reaped by pg-boss after their lease expires.
- The `job_runs` row's `started_at` minus `now()` tells you how long it's been "running". If it's hours and the queue is otherwise idle, the job is orphaned — cancel it from the Operations page.

## "Failures" count keeps growing

Open the **Failures** section on the Operations page and read the actual errors. Common patterns:

- **One bad file** repeatedly failing the probe job → fix or delete the file.
- **Disk full** under `/data` → free space, then retry.
- **External service down** (provider API, StashBox endpoint) → the underlying retry policy will eventually mark them permanently failed; clear and retry once the upstream is back.

After resolution, click **Clear all** to remove the rows from the dashboard. They stay in the `job_runs` table for audit but stop showing up in the count.

## The Identify page is slow

Identify runs candidate fetches in parallel up to a small concurrency. With many rows, the wait is API-bound, not Prismedia-bound.

- **Pick a single provider** at a time. Running against multiple providers serializes through the same UI.
- **Use Auto-accept** for singleton matches so you're not clicking through obvious ones.
- **Bulk identify** from a list page narrows the scope to selected items.

## How to file a useful bug report

Include:

- **Prismedia version** — Settings → Diagnostics or the GitHub Releases page.
- **Container logs around the failure** — `docker compose logs prismedia --tail 500 > prismedia.log`.
- **What you did** — exact steps from a known good state.
- **What you expected vs what happened.**
- **Whether `/data` is on a host bind, a named volume, or a network mount** — disk semantics affect a surprising number of bugs.

Open issues at [github.com/pauljoda/Prismedia/issues](https://github.com/pauljoda/Prismedia/issues).

## Useful queries

```sql
-- Recent failures
SELECT queue_name, target_label, error, finished_at
FROM job_runs WHERE status = 'failed'
ORDER BY finished_at DESC LIMIT 20;

-- Longest-running jobs (active or finished)
SELECT queue_name, target_label, finished_at - started_at AS duration
FROM job_runs WHERE finished_at IS NOT NULL
ORDER BY duration DESC LIMIT 20;

-- Videos missing fingerprints
SELECT id, file_path FROM video_episodes WHERE oshash IS NULL OR md5 IS NULL;
SELECT id, file_path FROM video_movies   WHERE oshash IS NULL OR md5 IS NULL;

-- Videos missing thumbnails
SELECT id, file_path FROM video_episodes WHERE thumbnail_path IS NULL;

-- Plugin auth (encrypted; just to confirm the row exists)
SELECT plugin_id, auth_key, length(encrypted_value) FROM plugin_auth;

-- Cache footprint by entity (run against /data via du)
-- du -sh /data/cache/hls/* | sort -h | tail -20
```
