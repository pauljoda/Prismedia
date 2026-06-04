---
sidebar_position: 1
title: Install & Run
description: Run Prismedia with Docker, mount your media, and open the app.
---

# Install & Run

Prismedia runs as a single container. You provide two mounts:

- `/data` for the database, generated assets, cache, plugin state, and the encryption secret.
- `/media` for the media folders you want Prismedia to scan.

## Requirements

- Docker or Docker Compose.
- A host folder (or named volume) for persistent `/data`.
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

Mount `/media` read-write when you want to use the **Files** workspace for browser uploads, folder creation, rename, move, delete, and scan-exclusion cleanup.

## Environment variables

Most installs need **no** environment variables. The container generates everything it needs on first boot. Set these only for the cases noted.

| Variable | Default | Use |
| --- | --- | --- |
| `PRISMEDIA_SECRET` | Auto-generated, persisted to `/data/.prismedia-secret` | Encryption key for plugin credentials (e.g. provider API keys) stored at rest. The container creates and persists one automatically, so it survives container recreation as long as `/data` persists. Set it explicitly only if you want to control or rotate the key yourself. See [Authentication & API Keys](../deployment/authentication.md). |
| `ASPNETCORE_URLS` | `http://0.0.0.0:8008` | Override the in-container listen address/port. |
| `PRISMEDIA_HLS_TRANSCODER` | `auto` | Force a transcoder profile (`auto`, `software`, hardware encoders). See [HLS Streaming](../developers/hls-streaming.md). |
| `PRISMEDIA_VAAPI_DEVICE` | `/dev/dri/renderD128` | VAAPI render node for hardware transcoding. |

:::note
There is no `PUBLIC_ORIGIN` variable. When Prismedia runs behind a reverse proxy you configure the proxy, not Prismedia — see [Reverse Proxy & Auth Middleware](../deployment/reverse-proxy.md).
:::

`DATABASE_URL`, `PRISMEDIA_DATA_DIR`, and `PRISMEDIA_CACHE_DIR` are managed by the unified image's entrypoint and point at the embedded PostgreSQL and the `/data` volume. You only set these when running the API/worker outside the unified image (see [Contributing](../developers/contributing.md)).

## API access

By default the app is open on your LAN — anyone who can reach `http://host:8008` can use the web UI. The browser app authenticates itself automatically with a same-origin, HttpOnly cookie, so normal use is frictionless.

The **`/api/*` routes and the Jellyfin-compatible routes require a key.** A human-typeable API key is generated on first boot and shown in **Settings → API Access**, where you can reveal, copy, or regenerate it. You need it for external API calls and for signing in from Jellyfin clients. See [Authentication & API Keys](../deployment/authentication.md).

## Image tags

| Tag | Meaning |
| --- | --- |
| `latest` | Current promoted release. Recommended for normal installs. |
| `release` / `release-X.Y.Z` | Release channel and version-pinned release image. |
| `beta` / `beta-X.Y.Z` | Manual beta channel image. |
| `alpha` / `alpha-X.Y.Z` | Manual alpha channel image. |
| `dev` | Newest `main` build after CI. Expect churn. |
| `sha-<short-sha>` / `X.Y.Z-<short-sha>` | Exact dev image for rollback or bisection. |

See [Upgrading & Rollback](../deployment/upgrading.md) for channel and migration details.

## What boots inside the container

| Process | Purpose |
| --- | --- |
| PostgreSQL 16 | Application data and durable job state (local-only, on `/data/postgres`). |
| .NET API | `/api/*`, the static Svelte app, Jellyfin-compatible routes, file streaming, HLS assets, migrations. |
| .NET worker | Scans, probes, thumbnails, sprites, waveforms, HLS, subtitles, identify, imports. The entrypoint supervises and auto-restarts it. |
| ffmpeg | Media probing, thumbnailing, HLS, subtitle extraction (bundled jellyfin-ffmpeg). |

The frontend is prebuilt into static assets and served by the .NET API. Normal installs do not run a separate web development server.

## Next steps

1. Open [Your First Library & Scan](./first-library.md).
2. Add a watched library root under `/media`.
3. Run a scan.
4. [Identify & enrich](./identify-walkthrough.md) the results.
