---
sidebar_position: 1
title: About Prismedia
description: What Prismedia is, what it manages, and how the pieces fit together.
---

# About Prismedia

Prismedia is a private, self-hosted home for movies, series, music, books, audiobooks, comics, images, and galleries. It is built for a trusted user or household on a private LAN.

Each item has one place in the library, from finding and identifying it to importing its files and watching, reading, or listening. Prismedia calls that item an **Entity**. People, studios, tags, and collections connect items across media types.

It ships as one Docker image. PostgreSQL 16, ffmpeg, the .NET API, the .NET worker, and the static Svelte frontend all run together behind port `8008`.

![Prismedia dashboard](/img/screenshots/dashboard.png)

## What it is for

- Keeping a local media library organized without handing library state to a cloud service.
- Browsing movies, series, videos, images, galleries, comics, eBooks, and audio — with people, studios, tags, and collections that link it all together — from one app.
- Reading comics (`.cbz`/`.zip`), EPUBs, and PDFs in a built-in reader, and playing video and audio with resume.
- Managing files and scan exclusions from the browser when your media mount is writable.
- Running local background work for scans, probes, thumbnails, sprites, waveforms, HLS, subtitles, identify, and imports.
- Identifying and enriching metadata through native plugins and wrapped Stash community scrapers, while keeping Prismedia's schema independent.
- Discovering media through metadata plugins, searching your configured indexers, and sending releases to your download clients. Prismedia tracks the transfer and imports the files onto the requested item.
- Playing video and audio through Prismedia's browser and native playback APIs, with per-user progress and history.

## Main workspaces

| Workspace | Purpose |
| --- | --- |
| **Dashboard** | Continue Watching, Recently Watched, recent media by type, library counts, and update notices. |
| **Browse** | Movies, Series, Videos, Galleries, Images, Comics, eBooks, Audio, Artists, People, Studios, Tags, and Collections. |
| **Files** | Watched-root file tree with open, upload, new folder, rename, move, rescan, exclude, and delete actions. |
| **Identify** | Durable review queue for provider matches and metadata proposals. |
| **Request** | Discover media through plugins, request items, review releases, and follow downloads and imports. |
| **Plugins** | Native plugins and wrapped Stash community scrapers. |
| **Jobs** | Worker heartbeat, active queues, recent work, failures, and manual queue actions. |
| **Settings** | Library roots, user accounts, playback, subtitles, generation, worker, storage, and diagnostics. |

The sidebar is yours to rearrange — rename, reorder, group, hide, and collapse sections — and your layout is saved on the server and follows you across devices. See [Navigation & Mobile Gestures](./using/navigation.md).

## Design direction

Prismedia's visual system turns its name into the interface: a neutral, dark app shell separates into spectrum identities for each media family, real artwork colors detail-page atmosphere, and frosted glass is reserved for floating chrome and controls.

The design language is documented in [Design Language](./developers/design-language.md).

## Runtime model

```text
Browser / native client / LAN
    │
    │ HTTP :8008
    ▼
.NET API
    ├─ serves /api/*
    ├─ serves the built Svelte app
    ├─ serves native playback routes under /api/playback
    ├─ streams direct files and HLS assets
    └─ applies EF Core migrations
    │
    ├──────────► PostgreSQL 16
    │
    └──────────► .NET worker
                 scans, probes, previews, HLS, subtitles, identify, imports
```

The frontend is a client only. Public HTTP contracts live in the .NET backend, and the Svelte app prefers generated OpenAPI clients.

## What to read next

- [Install & Run](./getting-started/install.md) gets the container running.
- [Organize Your Media Folders](./getting-started/organize-folders.md) explains mounts, watched roots, and recommended layouts.
- [Your First Library & Scan](./getting-started/first-library.md) walks through the first scan.
- [Identify & Enrich Your Media](./getting-started/identify-walkthrough.md) adds metadata and artwork.
- [Library & Scanning](./library/overview.md) explains exactly how folder layout becomes media.
- [Playback And Reading](./using/playback.md) covers video, audio, books, progress, and resume state.
