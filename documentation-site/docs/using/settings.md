---
sidebar_position: 8
title: Settings
description: Watched libraries, visibility, playback, subtitles, generation, storage, request services, worker, API access, and diagnostics.
---

# Settings

Settings is where Prismedia describes and edits app-wide behavior. Most controls are descriptor-driven so the UI, defaults, and persistence stay aligned.

![Settings](/img/screenshots/settings.png)

## Watched libraries

A watched library root is a container path plus scan behavior. Typical examples:

| Root | Scan toggles |
| --- | --- |
| `/media/movies` | Videos |
| `/media/images` | Images |
| `/media/books` | Books |
| `/media/music` | Audio |

Per-root settings:

| Setting | Meaning |
| --- | --- |
| **Enabled** | Disabled roots stay configured but are skipped. |
| **Recursive** | Walk subfolders. |
| **Scan videos / images / books / audio** | Independent media-type scanners. |
| **NSFW** | Marks media under the root as restricted by default. |
| **Auto Identify** | Whether this root participates in automatic identification during scans. |

How files become entities is covered in [Library & Scanning](../library/overview.md).

## Content visibility

Visibility controls whether NSFW content appears in browse pages, search, files, identify, plugin providers, relationship surfaces, and Jellyfin clients. Modes are designed for private LAN use:

- Hide restricted content by default.
- Show restricted content when explicitly enabled.
- Optionally auto-enable on trusted LAN access.

For Jellyfin clients, visibility is set **per profile** instead — see [Jellyfin Profiles](../jellyfin/profiles.md).

## Playback

Playback settings cover direct/stream-copy/HLS behavior, adaptive transcoding (defaults to Auto), generated preview defaults, player preferences, and resume behavior.

## Subtitles

![Subtitle view options](/img/screenshots/settings-subtitles.png)

Subtitle settings control:

- Auto-enable on playback.
- Preferred language order.
- Caption style, text size, vertical position, and transparency.

Video pages can apply local per-browser overrides from the player.

## Generation pipeline

Generation settings control background work such as thumbnails, sprites, trickplay tiles, waveforms, subtitle extraction, fingerprints, and HLS output. These affect future scans and rebuild actions.

**Fingerprints** are split into two independent toggles:

| Fingerprint | Default | Notes |
| --- | --- | --- |
| **OpenSubtitles hash (oshash)** | On | Reads only a small slice of each file — cheap. |
| **MD5 checksum** | Off | Must read every byte; the slow part of fingerprinting on large libraries. Turn it on only if you need it. |

## Generated storage

Generated-storage diagnostics help you understand and refresh cached assets under `/data` — thumbnails, sprites, trickplay tiles, HLS renditions, waveform data, plugin artwork, and extracted subtitles.

## Request services

Connect Radarr, Sonarr, and Lidarr instances for the [Request](./requests.md) workflow. Each service needs its URL and API key, and a **required connection test** verifies it and pulls its root folders, quality/metadata profiles, and tags before defaults can be chosen and the service saved. Per-service defaults cover the root folder, quality profile, search-on-request behavior, Arr tags applied to every request, and (Radarr) minimum availability. Multiple instances of the same type are supported; one per type is the default.

## Worker

Worker settings include concurrency and scheduling behavior. Higher concurrency helps on large machines and hurts on small disks; raise it carefully. Changes apply without a restart.

## API access

Manage the app **API key** (reveal, copy, regenerate) and **Jellyfin profiles** (create/edit/delete fake users with per-profile NSFW visibility and an enabled toggle). The API key authenticates `/api/*` calls and Jellyfin sign-in. See [Authentication & API Keys](../deployment/authentication.md) and [Jellyfin Profiles](../jellyfin/profiles.md).

## Diagnostics

Diagnostics show build/version/channel information, runtime state, update checks, storage actions, and maintenance controls. Include this information when filing bugs.
