---
sidebar_position: 6
title: Jobs & Operations
description: Worker status, queues, scans, failures, and maintenance.
---

# Jobs & Operations

The **Jobs** page shows what Prismedia is doing in the background. It is the first place to check when a scan, thumbnail, identify run, HLS render, subtitle extraction, or import feels slow.

![Jobs](/img/screenshots/jobs.png)

## Worker status

The worker heartbeat badge tells you whether the .NET worker is online. If the API is running but the worker is offline, you can still browse existing data, but queued work won't move. In the unified image the entrypoint supervises the worker and restarts it automatically if it ever stops.

## Queue families

| Queue | Typical work |
| --- | --- |
| **Library scan** | Walk watched roots, classify files, remove missing files. |
| **Media probe** | Read technical metadata: duration, dimensions, codecs, audio info, embedded tags. |
| **Preview** | Generate thumbnails, sprites, trickplay tiles, and waveforms. |
| **HLS** | Create adaptive playback assets on demand. |
| **Subtitles** | Extract embedded subtitles and normalize tracks. |
| **Identify** | Provider searches, bulk identify, and cascade child resolution. |
| **Collections** | Refresh dynamic and hybrid collection rules. |
| **Maintenance** | Cleanup and diagnostic backfills. |

## Running a scan

Run a scan from:

- **Jobs**, for a general library scan.
- **Settings → Watched Libraries**, for root-level management.
- **Files**, for a specific root, folder, or file context.

Scans are idempotent and **incremental** — unchanged roots skip the detailed work. There is at most one scan per media kind in flight; a scheduled scan, a new folder, and a manual scan reuse the in-flight one. New media gets its metadata and cover thumbnail first, before heavier preview/trickplay generation. See [How Scanning Works](../library/overview.md).

## Failures

Open a failure row to read the error message. Common causes:

- A bad or unsupported media file.
- A media path that no longer exists.
- A read-only mount for an operation that needs write access.
- Disk full under `/data`.
- Plugin network/API errors.

After fixing the cause, retry the action or rescan. Clearing failures hides acknowledged rows from the dashboard; it does not erase historical `job_runs` records.

## Stuck work

If work appears stuck:

1. Check the worker heartbeat.
2. Read the active queue row for the target label and message.
3. Check container logs: `docker compose logs prismedia --tail 200`.
4. Restart the container if the worker was killed mid-job — stale running jobs are recovered on the next boot after their lease expires.

## Worker concurrency

The worker concurrency setting is global; changing it takes effect without a restart. Raising it can speed up independent jobs but increases disk, CPU, and ffmpeg pressure. Keep it modest on small NAS or single-board systems. Background generation runs at below-normal priority with a capped thread count so it doesn't starve playback and browsing.

## Generated storage

Generated assets live under `/data`: thumbnails, HLS renditions, waveform data, sprites, trickplay tiles, extracted subtitles, and plugin artwork. **Settings → Generated Storage** offers diagnostics and rebuild actions when assets need refreshing.
