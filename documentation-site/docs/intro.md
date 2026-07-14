---
sidebar_position: 1
title: About Prismedia
description: What Prismedia is, what it manages, and how the pieces fit together.
---

# About Prismedia

Prismedia is a private, self-hosted media library for a trusted user or household on a private LAN. It is video-first, but movies, series, images, galleries, comics, eBooks, audio, people, studios, tags, and collections are all first-class library entities.

It ships as one Docker image. PostgreSQL 16, ffmpeg, the .NET API, the .NET worker, and the static Svelte frontend all run together behind port `8008`.

![Prismedia dashboard](/img/screenshots/dashboard.png)

## What it is for

- Keeping a local media library organized without handing library state to a cloud service.
- Browsing movies, series, videos, images, galleries, comics, eBooks, and audio — with people, studios, tags, and collections that link it all together — from one app.
- Reading comics (`.cbz`/`.zip`), EPUBs, and PDFs in a built-in reader, and playing video and audio with resume.
- Managing files and scan exclusions from the browser when your media mount is writable.
- Running local background work for scans, probes, thumbnails, sprites, waveforms, HLS, subtitles, identify, and imports.
- Identifying and enriching metadata through native plugins and wrapped Stash community scrapers, while keeping Prismedia's schema independent.
- Requesting new movies, series, and music through connected **Radarr, Sonarr, and Lidarr** instances, with request history and live download status.
- Optionally serving your library to **Jellyfin client apps** (Infuse, Manet, and similar) for video and audio playback.

## Main workspaces

| Workspace | Purpose |
| --- | --- |
| **Dashboard** | Continue Watching, Recently Watched, recent media by type, library counts, and update notices. |
| **Browse** | Movies, Series, Videos, Galleries, Images, Comics, eBooks, Audio, Artists, People, Studios, Tags, and Collections. |
| **Files** | Watched-root file tree with open, upload, new folder, rename, move, rescan, exclude, and delete actions. |
| **Identify** | Durable review queue for provider matches and metadata proposals. |
| **Request** | Search connected Radarr/Sonarr/Lidarr instances and request movies, series, artists, and albums. |
| **Plugins** | Native plugins and wrapped Stash community scrapers. |
| **Jobs** | Worker heartbeat, active queues, recent work, failures, and manual queue actions. |
| **Settings** | Library roots, user accounts, playback, subtitles, generation, worker, storage, and diagnostics. |

The sidebar is yours to rearrange — rename, reorder, group, hide, and collapse sections — and your layout is saved on the server and follows you across devices. See [Navigation & Mobile Gestures](./using/navigation.md).

## Design direction

Prismedia's visual system turns its name into the interface: a neutral, dark app shell separates into spectrum identities for each media family, real artwork colors detail-page atmosphere, and frosted glass is reserved for floating chrome and controls.

The design language is documented in [Design Language](./developers/design-language.md).

## Runtime model

```text
Browser / LAN / Jellyfin client
    │
    │ HTTP :8008
    ▼
.NET API
    ├─ serves /api/*
    ├─ serves the built Svelte app
    ├─ serves Jellyfin-compatible routes (/Items, /Videos, /Audio, …)
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
- [Your First Library & Scan](./getting-started/first-library.md) walks through the first scan.
- [Identify & Enrich Your Media](./getting-started/identify-walkthrough.md) adds metadata and artwork.
- [Library & Scanning](./library/overview.md) explains exactly how folder layout becomes media.
- [Jellyfin Compatibility](./jellyfin/overview.md) connects Infuse, Manet, and other clients.
