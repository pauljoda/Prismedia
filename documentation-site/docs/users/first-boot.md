---
sidebar_position: 2
title: First Boot
description: From a fresh container to a scanned library.
---

# First Boot

This page walks through what happens the first time you open Prismedia: adding library roots, kicking off the first scan, and verifying it landed.

## 1. Open the app

After `docker compose up -d` completes, open [http://localhost:8008](http://localhost:8008). A fresh install opens to an empty dashboard with no library roots.

If you're upgrading an existing install instead of starting fresh, read [Upgrading](./upgrading.md) first.

## 2. Add a library root

Open **Settings** (sidebar bottom-left) and find the **Watched Libraries** card.

![Settings — Watched Libraries panel](/img/screenshots/settings.png)

Click **Add library root** and fill in:

| Field | Meaning |
| --- | --- |
| **Path** | A path **inside the container**, e.g. `/media/movies`. |
| **Label** | Optional friendly name shown in the UI. |
| **Recursive** | Walk subdirectories. Almost always **on**. |
| **Scan videos / images / audio** | Independent scan-type flags. Pick what's in this root. |
| **NSFW** | Mark every entity discovered under this root as NSFW. |

Repeat for each root you want Prismedia to watch. The path you enter is the path the container sees, **not** the host path — so if you mounted `/srv/movies:/media/movies`, the root path is `/media/movies`.

:::tip
Read [Library Organization](./library-organization.md) before scanning. The depth of files under a root determines whether they become **movies**, **flat-series episodes**, or **seasoned-series episodes**. Adjusting after the fact means a rescan and re-identify pass.
:::

## 3. Configure global library settings

The other panels on the Settings page govern how scans behave and what gets generated. Sensible defaults are set out of the box; the ones worth thinking about up front:

| Setting | Default | What to consider |
| --- | --- | --- |
| **Auto-scan enabled** | off | Turn on once your library layout is stable. |
| **Scan interval (minutes)** | 60 | Frequency of recurring scans. |
| **Auto-generate previews** | on | Required for HLS playback to feel snappy. |
| **Generate trickplay** | on | Sprite sheet for hover-scrub on the timeline. |
| **Background worker concurrency** | 1 | Bump for faster generation, costs CPU. |
| **Default playback mode** | direct | Switch to `hls` if your clients can't seek directly. |

The full reference is in [Settings](./settings.md). Defaults are fine for a first scan.

## 4. Run the first scan

Open the **Operations** page (sidebar → **Jobs**) — this is where you watch and trigger background work.

![Operations — Jobs dashboard](/img/screenshots/jobs.png)

The page is grouped into queues by concern:

- **Library scans** — discover files in your roots
- **Library maintenance** — clean up generated assets
- **Video media pipeline** — probe, fingerprint, preview, sprites, HLS prep
- **Metadata import** — apply scrape/identify results
- **Gallery image pipeline** — image fingerprints, thumbnails
- **Audio pipeline** — probe, fingerprint, waveform

Find the **Library scan** queue card and click **Run**. The card shows live counts of running, backlog, and failed jobs. As the scan finds files, the **Video media pipeline** queues will fill in behind it (probe → fingerprint → preview).

You don't need to watch all of it — the worker handles things. Watch for **Failures**: any non-zero count means a file couldn't be processed. Click in for the full error.

:::info
On a large library the first scan can take a long time — gigabytes of preview clips and trickplay sprites get generated. You can keep using the app while this runs; the dashboard and library pages update as previews land.
:::

## 5. Verify

Hop to **Videos** in the sidebar. You should see your scanned files with:

- Thumbnails (placeholder gradient if previews are still pending)
- Resolution, codec, and duration metadata from `ffprobe`
- An **S01E03** badge on episodes (when season + episode numbers were parsed from the filename)

Hover a thumbnail to scrub through trickplay sprites. Click to open the detail page and play.

## 6. Identify (optional but recommended)

Filenames give you titles and episode numbers, but not posters, descriptions, performers, or studios. That's what **Identify** does.

The fastest path: open **Identify** in the sidebar, choose the **Videos** or **Series** tab, pick a provider (TMDB, TVDB, MusicBrainz, a Stash community scraper, or a StashBox endpoint depending on what's installed), and click **Run**. Each row gets one or more candidate matches; you accept or reject per row, or **Auto-accept** singletons.

The full workflow is in [Identify & Scrape](./identify-and-scrape.md), including the cascade flow that maps a series to its seasons and episodes in one pass.

---

You're up. The next pages cover what's where in the UI ([Browsing](./browsing.md)), how playback and the lightbox work ([Playback](./playback.md)), and how Identify actually flows ([Identify & Scrape](./identify-and-scrape.md)).
