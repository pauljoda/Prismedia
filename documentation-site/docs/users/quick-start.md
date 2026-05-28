---
sidebar_position: 1
title: Quick Start
description: Run Prismedia with Docker and open the app.
---

# Quick Start

Prismedia runs as a single container. You provide two mounts:

- `/data` for the database, generated assets, cache, plugin state, and app state.
- `/media` for the media folders you want Prismedia to scan.

## Requirements

- Docker or Docker Compose.
- A host folder for persistent `/data`.
- One or more readable media folders.
- Port `8008` available on the host.

## Docker run

```bash
docker run -d \
  --name prismedia \
  -p 8008:8008 \
  -v prismedia-data:/data \
  -v /path/to/your/media:/media \
  ghcr.io/pauljoda/prismedia:latest
```

Open [http://localhost:8008](http://localhost:8008).

## Docker Compose

```yaml
services:
  prismedia:
    image: ghcr.io/pauljoda/prismedia:latest
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
```

## Writable vs read-only media

Mount `/media` read-only when Prismedia should only scan and play your files:

```yaml
volumes:
  - /path/to/your/media:/media:ro
```

Mount `/media` read-write when you want to use the Files workspace for browser uploads, folder creation, rename, move, delete, and scan-exclusion cleanup.

## Environment

Most installs can start with no environment variables. Add these when you need durable secrets or a known host URL:

| Variable | Use |
| --- | --- |
| `PRISMEDIA_SECRET` | Encrypts plugin and StashBox credentials. Set this before entering API keys. |
| `PUBLIC_ORIGIN` | Public URL shown to clients when Prismedia is behind a proxy. |

## Image tags

| Tag | Meaning |
| --- | --- |
| `latest` | Current promoted release. Recommended for normal installs. |
| `release` / `release-X.Y.Z` | Release channel and version-pinned release image. |
| `beta` / `beta-X.Y.Z` | Manual beta channel image. |
| `alpha` / `alpha-X.Y.Z` | Manual alpha channel image. |
| `dev` | Newest `main` build after CI. Expect churn. |
| `sha-<short-sha>` | Exact dev image for rollback or bisection. |

## What boots inside the container

| Process | Purpose |
| --- | --- |
| PostgreSQL 16 | Application data and durable job state. |
| .NET API | `/api/*`, static Svelte app, file streaming, HLS assets, migrations. |
| .NET worker | Scans, probes, thumbnails, sprites, HLS, subtitles, identify, imports. |
| ffmpeg | Media probing, thumbnailing, HLS, subtitle extraction. |
| `prismedia-phash` | Stash-compatible video perceptual hashes. |

The frontend is prebuilt into static assets for release images and served by the .NET API. Normal installs do not run a separate web development server.

## Next steps

1. Open [First Boot](./first-boot.md).
2. Add a watched library root under `/media`.
3. Run a scan.
4. Browse the results from Dashboard, Videos, Files, or Search.
