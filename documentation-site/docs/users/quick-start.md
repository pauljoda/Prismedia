---
sidebar_position: 1
title: Quick Start
description: Run Prismedia with Docker in five minutes.
---

# Quick Start

Prismedia runs as one Docker image. PostgreSQL 16, ffmpeg, audiowaveform, the .NET API, the built Svelte web UI, and the background worker are all bundled. You provide two volumes — `/data` for application state and `/media` for your library — and one port (`8008`).

## Requirements

- **Docker** 24 or newer (Docker Desktop, Colima, or a Linux Docker engine)
- **2 GB of RAM** for the container itself; transcoding spikes can push higher
- **Disk** for cache and database under `/data` — plan for ~5–10% of your library size
- A **private network**. Prismedia ships with no authentication; do not expose port 8008 to the public internet.

## One-line Docker run

```bash
docker run -d \
  --name prismedia \
  -p 8008:8008 \
  -v prismedia-data:/data \
  -v /path/to/your/media:/media \
  ghcr.io/pauljoda/prismedia:latest
```

Open [http://localhost:8008](http://localhost:8008) and the dashboard loads. (On a fresh install you'll be sent through [First Boot](./first-boot.md) instead.)

## Docker Compose

The compose form is what most users run long-term — it's easier to update, restart, and pin.

```yaml title="docker-compose.yml"
services:
  prismedia:
    image: ghcr.io/pauljoda/prismedia:latest
    container_name: prismedia
    ports:
      - "8008:8008"
    volumes:
      - prismedia-data:/data
      - /path/to/your/media:/media
    restart: unless-stopped

volumes:
  prismedia-data:
```

```bash
docker compose up -d
docker compose logs -f prismedia   # follow startup
```

To upgrade later:

```bash
docker compose pull && docker compose up -d
```

## Volumes

| Mount | Purpose | Notes |
| --- | --- | --- |
| `/data` | PostgreSQL data and generated cache (HLS, thumbnails, sprites, waveforms). | Use a named volume or a host bind mount on a fast disk. |
| `/media` | Your media library. | Read-only is supported; if you want Prismedia to upload files to a folder, mount it read-write. |

You can mount **multiple** library roots. The simplest pattern is to mount each top-level library directly:

```yaml
volumes:
  - prismedia-data:/data
  - /srv/movies:/media/movies
  - /srv/series:/media/series
  - /srv/galleries:/media/galleries
```

Then add `/media/movies`, `/media/series`, and `/media/galleries` as separate library roots in **Settings → Watched Libraries**. Each root has its own scan-type flags (videos / images / audio) and its own NSFW default.

:::tip
Read [Library Organization](./library-organization.md) **before** pointing Prismedia at a real library. Folder depth determines how files become movies, flat series, or seasoned series — getting the layout right up front saves a lot of cleanup later.
:::

## Ports & networking

Only port `8008` is exposed. Prismedia serves the web UI, same-origin `/api/*` routes, and streaming endpoints from the .NET API process; there is no separate web server, nginx, or Redis.

If you want the app on a different host port:

```yaml
ports:
  - "9000:8008"   # host:container
```

Behind a reverse proxy (Caddy, Traefik, nginx) just point at `http://prismedia:8008`. WebSocket support is not required.

## Image tags

| Tag | What it pins | Use it when |
| --- | --- | --- |
| `latest` | The most recent stable release. | Normal installs. |
| `X.Y.Z` (e.g. `1.0.0`) | One exact release. | You want to pin a known-good version. |
| `X.Y` (e.g. `1.0`) | The latest patch on a minor line. | You want bug fixes but not minor-version churn. |
| `X` (e.g. `1`) | The latest minor on a major line. | Long-running deployment with the broadest tracking band. |
| `dev` | The newest commit on `main`. | You're testing an unreleased change and accept breakage. |
| `sha-abc1234` | One exact dev build. | Rollback or pinned dev testing. |

The `dev` tag is rebuilt on every push to `main`; `latest` only moves when a release is cut. See [Upgrading](./upgrading.md) for the full release/version policy.

## What boots inside the container

| Process | Role |
| --- | --- |
| **PostgreSQL 16** | Application data and the pg-boss job queue. Data lives at `/data/postgres`. |
| **.NET API** | Same-origin `/api/*`, streaming, persistence, and built web UI. Port `8008`. |
| **.NET Worker** | Background scan, probe, fingerprint, preview, HLS, and import jobs. |
| **ffmpeg / ffprobe** | Media probing, HLS transcoding, sprite generation. |
| **audiowaveform** | Audio waveform peak files. |
| **prismedia-phash** | Stash-compatible video perceptual hashes (Go binary). |

You don't manage these individually — the container runs them under one supervisor and exits as a unit.

## Next steps

1. Walk through [First Boot](./first-boot.md) — the breaking gate (if any), library roots, and your first scan.
2. Skim [Library Organization](./library-organization.md) so your folders match what Prismedia expects.
3. Bookmark the [Operations](./operations.md) page — that's where you watch jobs and re-run scans.
