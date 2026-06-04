---
sidebar_position: 3
title: Upgrading & Rollback
description: Image channels, version policy, migrations, backups, and how to roll back.
---

# Upgrading & Rollback

Prismedia publishes explicit image channels. This page covers what each tag means, how migrations apply, how breaking changes are announced, and how to roll back.

## Image tags

| Tag | What it pins | Use it when |
| --- | --- | --- |
| `latest` | Current release channel image. | Normal installs. Moves only when the release channel is manually promoted. |
| `release` | Current release channel image. | Same as `latest`, but explicit. |
| `release-X.Y.Z` | A release image for one build version. | Pin a known-good release build. |
| `beta` / `beta-X.Y.Z` | Current beta channel image. | Test a release candidate. |
| `alpha` / `alpha-X.Y.Z` | Current alpha channel image. | Test early builds. |
| `dev` | Newest commit on `main`. | Testing a change that hasn't been released. Accept churn. |
| `sha-abc1234` | One exact dev build. | Rollback or pinned dev testing. |
| `X.Y.Z-abc1234` | Dev build labelled with the build version. | Same as `sha-`, with the build version baked in. |

The in-app update checker follows your running channel: it knows whether you're on a dev/alpha/beta/release build and only lights up when a newer image exists **on that same channel**. The sidebar shows a small channel tag (dev/alpha/beta); release builds stay clean.

## Versioning policy

Prismedia follows [Semantic Versioning](https://semver.org/):

| Bump | What it means |
| --- | --- |
| **MAJOR** (`2.0.0`) | Breaking API changes, schema changes that need manual action, config-format changes. |
| **MINOR** (`1.1.0`) | New features, new API endpoints, new UI views. |
| **PATCH** (`1.0.1`) | Bug fixes, UI tweaks, dependency updates, docs. |

`package.json` carries the decided build version. Publishing alpha, beta, release, or dev images does not change that version.

## Where to find the changelog

Release notes live in [`CHANGELOG.md`](https://github.com/pauljoda/Prismedia/blob/main/CHANGELOG.md) and on the [GitHub Releases](https://github.com/pauljoda/Prismedia/releases) page. Every release section starts with a **What's New** block written for users — read it before upgrading.

## Routine upgrades

```bash
docker compose pull && docker compose up -d
```

Migrations apply on boot. The API owns and applies EF Core migrations; the worker waits for the database to be migrated before processing jobs.

If a migration fails, the container exits — Prismedia would rather refuse to start than serve a half-migrated database. The error appears in `docker compose logs prismedia`.

## Breaking upgrades

When a release would destroy data or require a rescan, the release notes call it out under **What's New** and the Keep a Changelog sections. For a breaking upgrade:

1. Snapshot `/data` before pulling the new image.
2. Read `CHANGELOG.md` for any rescan or manual-action instructions.
3. Start the new image and let EF Core migrations run on boot.
4. If the notes say to rebuild metadata, run a fresh scan from **Jobs → Library scan → Run**.

:::caution
If you upgrade and later want the old state back, the only way is restoring `/data` from a snapshot taken **before** the upgrade. Take the snapshot first.
:::

## Rolling back

If a release is broken on your setup:

1. **Stop the container.**
   ```bash
   docker compose down
   ```
2. **Restore `/data`** from the snapshot you took before the upgrade.
3. **Pin the previous version.**
   ```yaml
   image: ghcr.io/pauljoda/prismedia:release-1.0.0   # the previous release tag
   ```
4. **Bring it back up.**
   ```bash
   docker compose up -d
   ```

Without a `/data` snapshot, rollback after a forward-only migration isn't possible. Snapshot before any upgrade you can't easily redo.

## Backups

Prismedia ships no built-in backup tool. What works:

- **Volume snapshot** (ZFS, btrfs, LVM) of `/data` — cheap, instant, the safest option for downgrade. Includes the database, generated assets, plugin state, and the `PRISMEDIA_SECRET` file.
- **`pg_dump` of the embedded Postgres** — `docker exec` into the container; useful for a portable SQL dump.
- **`rsync`/`restic` of `/data`** — stop the container first for a consistent copy; Postgres files aren't safe to copy while running.

Your **media** (`/media`) doesn't need a Prismedia backup — it's read-only as far as Prismedia is concerned and lives wherever you keep it.

## Dev image discipline

The `dev` tag is rebuilt on every push to `main` and may carry schema changes that haven't reached a release.

1. A `dev` build can advance the schema. Going back to a release tag afterwards is a **downgrade** — restore a `/data` snapshot.
2. Channel images are promoted manually. Use `latest`/`release` for stable; `dev` for current `main`.

In short: `dev` is fine for testing, not for "leave it running and forget it."
