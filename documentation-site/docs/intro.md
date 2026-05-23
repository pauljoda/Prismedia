---
sidebar_position: 1
title: About Prismedia
sidebar_label: About Prismedia
description: A private, self-hosted home for your entire media collection — videos, comics, books, galleries, and audio.
---

# Prismedia

**A private screening room for your entire library.**

Prismedia is a self-hosted media library that brings your videos, movies, TV series, comics, manga, books, image galleries, and audio into one organized, searchable interface — running entirely on your own hardware, on your own network.

There are no cloud accounts, no subscriptions, and no data leaves your home. Everything runs inside a single Docker container. You provide the media; Prismedia handles the rest.

![Prismedia dashboard](/img/screenshots/dashboard.png)

## What Prismedia manages

| Media type | What you get |
| --- | --- |
| **Videos & Movies** | HLS adaptive streaming, on-demand transcoding, trickplay sprites, frame-strip scrubbing, markers. |
| **TV Series** | Season and episode organization inferred from your folder layout, with full metadata and per-episode progress. |
| **Comics & Manga** | cbz/zip archives and image folders organized into series, with natural page order, ComicInfo metadata, reading progress, and a dedicated paged or webtoon reader. |
| **Books** | Your reading collection alongside every other media type — browsable, searchable, and readable from any device. |
| **Image Galleries** | Folder-based and archive-based galleries with grid and lightbox modes, ratings, tags, and performer/studio linking. |
| **Audio** | Albums, tracks, cover art, waveforms, performer and studio linking, shuffle, and a built-in player. |
| **Performers, Studios & Tags** | Rich cross-referenced entities that span every media type — link a performer to their videos, audio, and galleries from one profile. |

## How it works

1. **Run the Docker image** — PostgreSQL, ffmpeg, and the web server ship as one container. No external dependencies.
2. **Mount your media** — point one or more directories at `/media` and register them as library roots in Settings.
3. **Scan** — Prismedia walks your library, fingerprints files, generates thumbnails and previews, and organizes everything into the appropriate library type.
4. **Identify** — run the identify engine to pull in metadata from plugins and scrapers. Titles, cover art, cast, ratings, and descriptions fill in automatically.
5. **Browse and play** — open the app from any browser on your local network.

## Key capabilities

### Metadata, everywhere

Every entity — video, comic, book, audio track, performer, studio — carries the same rich metadata surface: title, cover art, description, ratings, tags, and provenance. Plugin-powered providers handle identification automatically, with Stash-compatible scrapers and StashDB endpoints supported natively.

### Mobile-first

Every view is designed for a phone first. Browse, search, play, and read from any device on your network. Touch targets, gesture navigation, and bottom navigation are designed before the desktop expansion — not bolted on after.

### One container

PostgreSQL 16, ffmpeg, audiowaveform, and the background worker all run inside one Docker image. Mount `/data` for application state, mount your media under `/media`, expose port `8008`, and you're running. No environment variables required.

### Plugin system

Extend Prismedia with TypeScript or Python plugins that add metadata providers, scrapers, and identify sources. The community scraper index is built in — browse, install, and enable scrapers directly from the Settings page.

### Background jobs you can see

Scanning, probing, transcoding, and scraping all happen as background jobs. The Operations dashboard shows every running and queued job in real time so you always know what the system is doing.

---

## Get started

### New to Prismedia?

Start with the [Quick Start](./users/quick-start.md) guide. It walks through the one-command Docker install, volume setup, and first boot in about five minutes. Then read [First Boot](./users/first-boot.md) before pointing it at a real library.

### Coming from Stash?

Prismedia supports native StashDB endpoints and is compatible with community Stash scrapers. See [Stash Compatibility](./plugins/stash-compat.md) for migration guidance.

### Want to write a plugin?

Start at [Plugins · Overview](./plugins/overview.md). The [Manifest](./plugins/manifest.md) and [Capabilities](./plugins/capabilities.md) pages are the reference; the [TypeScript](./plugins/typescript-plugin.md) and [Python](./plugins/python-plugin.md) guides walk through real plugins end-to-end.

### Want to understand the code?

Start at [Architecture](./developers/architecture.md). [Monorepo Layout](./developers/monorepo.md), [Database](./developers/database.md), and [API & Jobs](./developers/api-and-jobs.md) drill into each layer.

---

:::tip Pre-1.0 note
Prismedia is under active development. We do not maintain backwards-compatibility shims between schema breaks. When something destructive changes, a one-time gate in the UI explains what's happening and asks for consent before proceeding. The [Upgrading](./users/upgrading.md) page covers the policy.
:::
