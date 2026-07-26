<p align="center">
  <img src="docs/logo.png" width="128" alt="Prismedia colored prism mark" />
</p>

<h1 align="center">Prismedia</h1>

<p align="center">
  <strong>Your whole media life. One private home.</strong>
  <br />
  A private, self-hosted media library for watching, reading, listening, requesting, and managing across web, iPhone, iPad, and Apple TV.
</p>

<p align="center">
  <a href="https://pauljoda.github.io/Prismedia/">
    <img alt="Visit the Prismedia website" src="https://img.shields.io/badge/Website-Prismedia-c7c9cc?style=for-the-badge&logo=googlechrome&logoColor=111214" />
  </a>
  <a href="https://pauljoda.github.io/Prismedia/docs/getting-started/install">
    <img alt="Quick start guide" src="https://img.shields.io/badge/Get_Started-Docker-202734?style=for-the-badge&logo=docker&logoColor=white" />
  </a>
  <a href="https://testflight.apple.com/join/c9bgDxr7">
    <img alt="Join the Prismedia TestFlight" src="https://img.shields.io/badge/Join-TestFlight-0d96f6?style=for-the-badge&logo=apple&logoColor=white" />
  </a>
  <a href="https://www.producthunt.com/products/prismedia?launch=prismedia">
    <img alt="Follow Prismedia on Product Hunt" src="https://img.shields.io/badge/Follow-Product_Hunt-da552f?style=for-the-badge&logo=producthunt&logoColor=white" />
  </a>
  <a href="https://github.com/pauljoda/Prismedia/pkgs/container/prismedia">
    <img alt="Container image" src="https://img.shields.io/badge/GHCR-prismedia-0b0e12?style=for-the-badge&logo=github&logoColor=white" />
  </a>
</p>

<p align="center">
  <a href="https://github.com/pauljoda/Prismedia/actions/workflows/ci.yml">
    <img alt="Prismedia CI status" src="https://github.com/pauljoda/Prismedia/actions/workflows/ci.yml/badge.svg?branch=main" />
  </a>
  <a href="https://github.com/pauljoda/Prismedia/actions/workflows/documentation-site.yml">
    <img alt="Prismedia documentation deployment status" src="https://github.com/pauljoda/Prismedia/actions/workflows/documentation-site.yml/badge.svg?branch=main" />
  </a>
</p>

<p align="center">
  <a href="https://pauljoda.github.io/Prismedia/">Website</a> &middot;
  <a href="https://pauljoda.github.io/Prismedia/docs/intro">Docs</a> &middot;
  <a href="#quick-start">Quick Start</a> &middot;
  <a href="https://testflight.apple.com/join/c9bgDxr7">TestFlight</a> &middot;
  <a href="https://www.producthunt.com/products/prismedia?launch=prismedia">Product Hunt</a> &middot;
  <a href="https://www.reddit.com/r/Prismedia/">Subreddit</a>
</p>

<p align="center">
  <img src="docs/showcase/01-one-private-home.png" alt="One private Prismedia library enters as white light and becomes purpose-built media experiences across the spectrum" width="100%" />
</p>

## One Library In. Every Experience Out.

Movies, music, books, audiobooks, comics, images, and galleries share the same underlying needs: identity, artwork, files, metadata, relationships, progress, and history. Self-hosted media usually spreads those needs across one app for playback, another for requests, and a suite of services for acquisition and maintenance.

Prismedia keeps that shared foundation in one private system, then gives every medium an interface shaped around how it is actually used. The logo makes the idea literal: **one light enters; every medium leaves in its own color.**

The complete web workspace, native iPhone and iPad experience, and focus-first Apple TV app share one household library. An item remains the same item while it moves from discovery and request through acquisition, identification, organization, playback or reading, and long-term maintenance.

The native Apple apps are open to a limited testing group through [TestFlight](https://testflight.apple.com/join/c9bgDxr7).

<p align="center">
  <a href="https://pauljoda.github.io/Prismedia/#launch-film-title">
    <img src="documentation-site/static/img/showcase/prismedia-launch-poster.webp" alt="Prismedia product film showing the web, iPhone, iPad, and Apple TV experiences" width="100%" />
  </a>
  <br />
  <a href="https://pauljoda.github.io/Prismedia/#launch-film-title"><strong>▶ Watch the 72-second silent product film</strong></a>
</p>

<p align="center">
  <a href="https://www.producthunt.com/products/prismedia?launch=prismedia">
    <img alt="Prismedia on Product Hunt" height="54" width="250" src="https://api.producthunt.com/widgets/embed-image/v1/featured.svg?post_id=1205980&amp;theme=dark" />
  </a>
</p>

## From “I Want It” To “Press Play”

Discovery, requests, acquisition, metadata, files, playback, reading, and listening are one connected lifecycle. Provider identity, download state, artwork, history, and personal progress stay attached to the same library item instead of disappearing at every handoff.

<p align="center">
  <img src="docs/showcase/02-one-media-lifecycle.png" alt="Prismedia connects discovery, requests, acquisition, management, and enjoyment in one media lifecycle" width="100%" />
</p>

## Quick Start

<p align="center">
  <img src="docs/showcase/06-self-hosted.png" alt="One self-hosted Prismedia image connects the web, iPhone, iPad, and Apple TV experiences for a household" width="100%" />
</p>

> [!IMPORTANT]
> **Prismedia is in early development.** Not every image tag is guaranteed to be published yet. The **`dev`** tag is always built (every push to `main`), and **`alpha`** is generally available. **`beta`**, **`release`**, and **`latest`** are promoted manually and **may not be available yet** — if `latest` can't be pulled, use `dev` (or `alpha`) for now. Expect rough edges and breaking changes while things stabilize.

### Docker Run

```bash
docker run -d \
  --name prismedia \
  -p 8008:8008 \
  -v prismedia-data:/data \
  -v /path/to/your/media:/media \
  ghcr.io/pauljoda/prismedia:latest
```

Open [http://localhost:8008](http://localhost:8008), add `/media` or one of its subfolders as a watched library, then run a scan from **Jobs** or **Settings**.

### Docker Compose

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

### Volumes

| Mount | Purpose |
| --- | --- |
| `/data` | PostgreSQL data, generated cache, thumbnails, waveforms, trickplay, HLS output, plugin state, encryption secret |
| `/media` | Your mounted media folders |

Mount `/media` read-only if Prismedia should only scan and play files. Mount it read-write if you want browser uploads, renames, moves, deletes, and file-manager organization.

### Access

Prismedia has real user accounts: a first-run wizard creates your administrator, and every household member gets their own username and password. The same credentials sign in to the web app, Jellyfin clients, and OPDS readers, and all `/api/*` routes require a signed-in user — so no reverse-proxy auth middleware is needed. Admins control per-user library access and NSFW visibility. See [Authentication & User Accounts](https://pauljoda.github.io/Prismedia/docs/deployment/authentication).

### Image Tags

| Tag | Use |
| --- | --- |
| `latest` | Current promoted release. Recommended for normal installs. |
| `release` / `release-X.Y.Z` | Release channel and version-pinned release images. |
| `beta` / `beta-X.Y.Z` | Manual beta channel for release candidates. |
| `alpha` / `alpha-X.Y.Z` | Manual alpha channel for early testing. |
| `dev` | Latest `main` build. Useful for testing fixes before release. |
| `sha-<short-sha>` / `X.Y.Z-<short-sha>` | Exact dev build for rollback or bisection. |

Read [CHANGELOG.md](CHANGELOG.md) before upgrading a library you care about.

## What Prismedia Manages

Video is the foundation, but music, audiobooks, eBooks, comics, images, and galleries are first-class media—not generic files forced into the same grid.

<p align="center">
  <img src="docs/showcase/05-purpose-built-media.png" alt="Prismedia gives video, music, audiobooks, eBooks, comics, images, and galleries purpose-built native experiences" width="100%" />
</p>

### Library And Search

Prismedia has dedicated browse surfaces for movies, series, videos, images, galleries, comics, eBooks, audio, artists, people, studios, tags, and collections. The dashboard leads with Continue Watching and Recently Watched; the search page and command palette jump across every entity type.

<p align="center">
  <img src="docs/screenshots/videos.png" alt="Video library" width="49%" />
  <img src="docs/screenshots/search.png" alt="Search" width="49%" />
</p>

### File Manager

The **Files** workspace mirrors watched library roots and gives you practical file operations without leaving the app: open linked entities, create folders, upload, rename, move, rescan, exclude paths from scans, remove exclusions, and delete when the media mount is writable.

<p align="center">
  <img src="docs/screenshots/files.png" alt="File manager" width="100%" />
</p>

### Playback And Reading

Videos direct-play when the client can decode them, stream-copy (remux) where possible, and fall back to on-demand HLS only when a transcode is truly needed. Detail pages include subtitles, transcript management, trickplay previews, resume, metadata editing, and artwork controls.

Comics (`.cbz`/`.zip`), EPUBs, and PDFs open in a built-in reader — paged and webtoon comics, reflowable EPUBs, and a full PDF reader with selectable text, zoom, search, outline, and resume. Images and galleries use a lightbox with metadata and linked entities. Audio plays through a persistent bar with a queue, shuffle, waveforms, and OS media-control integration.

<p align="center">
  <img src="docs/screenshots/video-detail.png" alt="Video detail" width="49%" />
  <img src="docs/screenshots/audio.png" alt="Audio playback" width="49%" />
</p>

### Metadata And Identify

The Identify workspace keeps a durable review queue. Add movies, series, videos, books, galleries, images, people, studios, or audio, run providers, review field-by-field proposals, choose artwork, walk into streaming child proposals (seasons/episodes, volumes/chapters, albums/tracks), and accept when the result is right. **Auto Identify** can apply confident matches automatically during scans.

Plugins can be native TypeScript or Python, and Stash community scrapers can be wrapped as providers.

<p align="center">
  <img src="docs/screenshots/identify.png" alt="Identify queue" width="49%" />
  <img src="docs/screenshots/plugins.png" alt="Plugins" width="49%" />
</p>

### Requests

Request is Prismedia's first-party acquisition workspace. Search for books, authors, movies, series, artists, and albums, then let Prismedia create Wanted library entities, search Prowlarr or direct Torznab/Newznab indexers, route releases to qBittorrent, Transmission, or SABnzbd, monitor the download, import the result into the right library, and keep durable History for every grab, import, failure, blocklist, and removal. Wanted and acquired items live on the same library pages with release picking, live progress, monitoring, Missing/Cutoff Unmet lists, and detail metadata from providers such as OpenLibrary, TMDB, and MusicBrainz.

<p align="center">
  <img src="docs/screenshots/requests.png" alt="Request search" width="49%" />
  <img src="docs/screenshots/request-detail.png" alt="Request detail" width="49%" />
</p>

### Jellyfin Clients (Experimental)

A Jellyfin-compatible API lets client apps discover Prismedia, sign in, and stream — tested with **Infuse** (video + audio) and music clients like **Manet**, **Finamp**, and **Symfonium**. Clients sign in with your Prismedia user accounts, and each user carries their own library access and NSFW visibility, so you can run separate SFW and NSFW "servers" in your client. Resume position and play counts sync both ways, per user. See [Jellyfin Compatibility](https://pauljoda.github.io/Prismedia/docs/jellyfin/overview).

### Web And Native Apple Apps

The responsive web app is the complete library workspace: browse every medium, request and identify items, manage files, tune settings, and inspect background work from one interface.

The native iPhone and iPad app adds adaptive Apple-platform navigation, video and audio playback, dedicated music and audiobook players, and customizable EPUB, PDF, comic, and webtoon reading. Tune typeface, size, weight, line spacing, margins, theme, and brightness. When a title has both text and audio editions, reading and listening progress stay together with separate actions for continuing each experience.

<p align="center">
  <img src="docs/showcase/04-custom-reader.png" alt="Prismedia's native reader with literary typography, deep customization, and combined reading and audiobook progress" width="100%" />
</p>

The Apple TV app uses a cinematic, focus-first interface and a custom native player built on the device's codec and playback stack. Supported sources direct-play at original quality—including lossless audio—while the paused chrome keeps title, stream state, resolution, codecs, timeline, audio, subtitles, and playback controls readable from the couch.

<p align="center">
  <img src="docs/showcase/03-native-playback.png" alt="Prismedia's custom Apple TV player showing direct play, original-quality video, lossless audio, native codecs, and paused playback controls" width="100%" />
</p>

The iPhone, iPad, and Apple TV apps are currently distributed through the limited [Prismedia TestFlight](https://testflight.apple.com/join/c9bgDxr7).

### Collections

Collections are simple groupings for browsing and curation. They can be manual, rule-driven, or hybrid, and they can contain movies, series, galleries, images, books, and audio tracks. They are not a global playback queue; they are an organizational view over your library.

<p align="center">
  <img src="docs/screenshots/collections.png" alt="Collections" width="100%" />
</p>

### Jobs, Settings, And Visibility

Long-running work runs in the .NET worker and is visible in **Jobs**: scans, probes, previews, thumbnails, sprites, waveforms, HLS, subtitles, identify, imports, collection refreshes, and maintenance. Settings control watched libraries, user accounts, playback, subtitles, generated storage, worker concurrency, and diagnostics.

<p align="center">
  <img src="docs/screenshots/jobs.png" alt="Jobs" width="49%" />
  <img src="docs/screenshots/settings.png" alt="Settings" width="49%" />
</p>

## Design Language

Prismedia's visual system makes the name literal: neutral white-light chrome holds the whole collection together, then each media family takes a muted color from the prism spectrum. True black and opaque material surfaces keep artwork dominant; a page gets one restrained accent moment, while frosted glass is reserved for navigation, toolbars, menus, dialogs, and other layers that actually float.

The design language lives in [docs/design-language.md](docs/design-language.md) and is mirrored in the [documentation site](https://pauljoda.github.io/Prismedia/docs/developers/design-language).

## Documentation

- [Install & Run](https://pauljoda.github.io/Prismedia/docs/getting-started/install)
- [Your First Library & Scan](https://pauljoda.github.io/Prismedia/docs/getting-started/first-library)
- [Identify & Enrich Your Media](https://pauljoda.github.io/Prismedia/docs/getting-started/identify-walkthrough)
- [Library & Scanning](https://pauljoda.github.io/Prismedia/docs/library/overview)
- [Requests (Radarr / Sonarr / Lidarr)](https://pauljoda.github.io/Prismedia/docs/using/requests)
- [Jellyfin Compatibility](https://pauljoda.github.io/Prismedia/docs/jellyfin/overview)
- [Reverse Proxy & Auth Middleware](https://pauljoda.github.io/Prismedia/docs/deployment/reverse-proxy)
- [Architecture](https://pauljoda.github.io/Prismedia/docs/developers/architecture)

## Development

### Prerequisites

- Node.js 22
- pnpm 10
- .NET 10 SDK
- Docker
- ffmpeg for media work outside the unified image

### Local Stack

```bash
pnpm install
docker compose -f infra/docker/docker-compose.yml up -d postgres
pnpm --filter @prismedia/web-svelte dev
dotnet run --project apps/backend/src/Prismedia.Api/Prismedia.Api.csproj
dotnet run --project apps/backend/src/Prismedia.Worker/Prismedia.Worker.csproj
```

Open the running application through the .NET host at [http://localhost:8008](http://localhost:8008). Vite provides frontend hot reload behind the development stack, but port `8008` is the canonical app surface and same-origin API entry point.

### Useful Commands

```bash
pnpm check          # frontend lint/typecheck through turbo
pnpm test:unit      # TypeScript unit tests
pnpm test:web-svelte
pnpm test:backend   # .NET tests
pnpm docs:check     # Docusaurus typecheck + build
pnpm release:check  # changelog + workspace version validation
```

### Build The Production Image

```bash
docker build -f infra/docker/unified.Dockerfile -t prismedia:local .
```

## Release Notes

Prismedia starts at `1.0.0` and uses plain SemVer versions. The root `package.json` is the build version and all workspace package versions must match it. Channel publishing never edits package versions or changelog headings; it only publishes the already-decided build.

See [CHANGELOG.md](CHANGELOG.md) for user-facing release notes.

## License

See [LICENSE](LICENSE).
