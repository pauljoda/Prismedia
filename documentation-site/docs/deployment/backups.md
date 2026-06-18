---
sidebar_position: 3
title: Backups & Restore
description: How Prismedia creates database backups, keeps retention, and performs destructive restores.
---

# Backups & Restore

Prismedia includes database backups from **Settings -> Database Backups**. They protect Prismedia's Postgres application state: media entities, settings, jobs, playback progress, Jellyfin profiles, request history, plugin state, and other database-backed records.

They do **not** copy your media files. Your `/media` mount should already live on your own storage and backup plan.

## What gets created

Backups are Postgres custom-format dumps created with `pg_dump`.

| Backup type | Created by | Retention |
| --- | --- | --- |
| Automatic | Prismedia runs one per day. | Kept for 7 days by default. |
| Manual | Click **Backup Now** in Settings. | Permanent until you delete the file yourself. |

The Settings card shows the newest backup, the next automatic run time, permanent manual count, and the backup files Prismedia knows about.

## Where files live

By default, database backups are stored under:

```text
/data/backups/database
```

You can move them by setting:

```bash
PRISMEDIA_BACKUP_DIR=/data/backups/database
```

For normal Docker installs, keep the directory under `/data` or another persistent volume. If the directory is inside the container filesystem instead of a mounted volume, backups disappear when the container is recreated.

## Before risky changes

Use **Backup Now** before:

- Adding or removing a library root that could delete scanned database records.
- Running a large cleanup or rescan.
- Testing a plugin that will write metadata broadly.
- Making a change you may want to undo quickly.

Manual backups are not part of the 7-day automatic retention window, so they are useful as named restore points around maintenance work.

## Restore from Settings

Restoring is destructive. The selected dump replaces the current database.

1. Open **Settings -> Database Backups**.
2. Pick a completed backup in **Restore File**.
3. Type `DESTROY AND RESTORE` exactly in the confirmation field.
4. Press **Restore** and confirm the browser warning.

Prismedia moves the browser to a restore progress page while the database is being replaced. When the restore finishes, the page returns to the dashboard automatically.

In production, Prismedia stages a restore request, stops the API, and relies on the container supervisor to start it again. On startup, the API applies the restore before normal request handling. In local development, the API applies the restore in the running process so `localhost:8008` does not stay shut down. The worker pauses while the restore marker exists, so background jobs do not claim new database work while a restore is pending.

:::caution
Do not restore unless you are certain. Anything created, scanned, watched, edited, requested, or configured after the selected backup was made will be replaced by the backup's older state.
:::

## What backups do not cover

Database backups are not full instance snapshots. They do not include:

- `/media` files.
- Generated thumbnails, waveforms, trickplay tiles, HLS cache, or other rebuildable files under `/data/cache`.
- A safe downgrade path after a forward-only schema migration.

For upgrades and rollbacks, take a host-level snapshot of `/data` before pulling the new image. A `/data` snapshot includes the database files, generated assets, plugin state, and the `.prismedia-secret` file used to decrypt stored credentials.

## Troubleshooting

Production Docker images include the Postgres client tools used for backup and restore.

For custom deployments, make sure `pg_dump` and `pg_restore` are available to the API process, or set explicit paths:

```bash
PRISMEDIA_PG_DUMP_PATH=/usr/bin/pg_dump
PRISMEDIA_PG_RESTORE_PATH=/usr/bin/pg_restore
```

For local development, Prismedia can fall back to the running Docker Compose `postgres` service when the API is running on the host and the database is on `localhost`.

Failed backup attempts appear in the Settings card with their error. Fix the tool path, database connection, or storage permissions, then press **Backup Now** again.

## See also

- [Upgrading & Rollback](./upgrading.md)
- [Authentication & API Keys](./authentication.md)
