# Jellyfin Client Parity Audit (Infuse)

Goal: make Prismedia's Jellyfin-compatibility layer a **1:1 replica** of real Jellyfin from a
client's point of view, so Infuse (and other Jellyfin clients) cannot tell the difference —
covering playback/resume/watched state, metadata, and image assets.

This document maps what a live Infuse session actually requests, what real Jellyfin returns, and
where Prismedia currently diverges. It is the working spec for closing the gaps.

## Methodology

- Tool: `scripts/dev/jellyfin-capture-proxy.mjs` — a zero-dependency logging reverse proxy. Infuse
  points at the proxy; the proxy forwards to an upstream (real Jellyfin **or** Prismedia) and logs
  the full request/response to JSONL. It modifies neither server and forwards auth untouched.
- **Mode A (ground truth):** upstream = real Jellyfin (`jellyfin.pauljoda.com`). Capture of a
  representative tour (home/resume shelf, a movie, a series→season→episode, playback with
  scrub/pause/resume/stop, watched toggles).
- **Mode B (our side):** upstream = Prismedia (`:8008`). Same tour, to diff Prismedia's actual
  output against ground truth.
- Proxy fix applied during this audit: it now strips `accept-encoding` from the upstream request
  (and decompresses as a fallback) so captured bodies are readable JSON rather than gzip bytes.

### Reference artifacts
- Ground-truth decoded responses: `~/jf-ground-truth.json` (movie item, resume, episodes, seasons,
  playbackinfo, items query, userviews, trailers, special features, media segments).
- Raw capture: `~/jf-real2.jsonl`.

## Endpoint coverage (Infuse-observed)

Prismedia's Jellyfin routes are mounted at the **server root** (not under `/api`).

### Implemented (24)
`/System/Info/Public`, `/System/Info`, `/System/Ping`, `POST /Users/AuthenticateByName`,
`/Users/{userId}`, `/Users/Me`, `/Users/Public`, `/UserViews`, `/UserViews/GroupingOptions`,
`/Library/VirtualFolders`, `/Items`, `/Items/{id}`, `/Items/Latest`, `/Users/{userId}/Items/Latest`,
`/Items/{id}/PlaybackInfo` (GET+POST), `/Items/{id}/Images/{type}`, `/Shows/{id}/Seasons`,
`/Shows/{id}/Episodes`, `/UserItems/Resume`, `/Users/{userId}/Items/Resume`, `/Videos/{id}/stream`,
HLS (`master.m3u8`, segments), Trickplay, `POST /Sessions/Playing[/Progress|/Stopped|/Ping]`,
`POST|DELETE /UserPlayedItems/{id}`, `POST /Sessions/Capabilities[/Full]`.

### Missing — Infuse calls these; Prismedia has no route
| Endpoint | Effect in Infuse |
| --- | --- |
| `GET /Shows/NextUp` | "Up Next" / Next Up shelf is empty |
| `GET /Items/{id}/LocalTrailers` | trailers row missing on detail page |
| `GET /Items/{id}/SpecialFeatures` | extras / bonus features missing |
| `GET /MediaSegments/{id}` | intro/credit skip markers (real server returned an empty list, but the call 404s today) |
| UserData write (favorite) | favorite toggle has no endpoint (watched works via `/UserPlayedItems`) |

### Behavioral note — `/DisplayPreferences`
Real Jellyfin uses the literal path `/DisplayPreferences/usersettings`. Prismedia matches it via a
templated `{displayPreferencesId}` route, which works but is worth confirming returns the expected
shape.

## Field gaps (vs ground truth)

### Item DTO (BaseItemDto) — missing fields
- **`ImageBlurHashes`** — not implemented anywhere in the backend. Real Jellyfin sends a blurhash
  per image, both item-level (`Primary`/`Backdrop`/`Logo`/`Thumb`) and per `People[]` entry. Infuse
  uses these for the blurred placeholder shown while artwork loads. **Highest-impact "feels
  different" gap.**
- `VideoType`, item-level `Container`, `Taglines`, `ProductionLocations`, `LocalTrailerCount`,
  `SpecialFeatureCount`, `CanDelete`, `EnableMediaSourceDisplay`, `DisplayPreferencesId`,
  `ChannelId` (null), `Trickplay`, `LockedFields`, `LockData`.

### Episode DTO — missing parent-image fields (drive Up-Next / episode artwork)
`ParentBackdropItemId`, `ParentBackdropImageTags`, `ParentLogoItemId`, `ParentLogoImageTag`,
`ParentThumbItemId`, `ParentThumbImageTag`, `SeriesPrimaryImageTag`, `IndexNumberEnd`.

### UserData — missing field
`ItemId` (Prismedia sends `Key` but omits `ItemId`). All other fields present and correct:
`PlayedPercentage`, `PlaybackPositionTicks`, `PlayCount`, `IsFavorite`, `LastPlayedDate`, `Played`,
`Key`.

### MediaStream — thin (matters for 4K HDR / Dolby Vision)
Prismedia's `JellyfinCatalogMediaStreamDto` exposes ~11 fields; real Jellyfin sends ~30, including
`VideoRange`, `VideoRangeType`, `ColorSpace`, `ColorTransfer`, `ColorPrimaries`,
`DvProfile`/`DvLevel`/`DvVersionMajor`/`DvVersionMinor`/`RpuPresentFlag`/`BlPresentFlag`,
`Profile`, `Level`, `PixelFormat`, `BitDepth`, `AspectRatio`, `RefFrames`, `IsAVC`,
`AudioSpatialFormat`, `ChannelLayout`, `Channels`, `SampleRate`, `DisplayTitle`.
**Note:** Prismedia already computes this data (`ApplicationContractMapping.cs`,
`HlsAssetService.cs`) — it's just not mapped into the Jellyfin DTO. This is a mapping job, not new
probing.

### MediaSource — thin
Missing vs ground truth: `ETag`, `VideoType`, `Bitrate`, `DefaultAudioStreamIndex`,
`ReadAtNativeFramerate`, `RequiresOpening`/`RequiresClosing`/`RequiresLooping`, `SupportsProbing`,
`GenPtsInput`, `IgnoreDts`/`IgnoreIndex`, `IsInfiniteStream`, `UseMostCompatibleTranscodingProfile`,
`HasSegments`, `MediaAttachments`, `Formats`, `RequiredHttpHeaders`, `TranscodingSubProtocol`.

## Verified-correct behavior (keep)
- **Images:** Infuse requests `/Items/{id}/Images/{type}?tag=<tag>` with **no resize params** —
  full-resolution, scaled client-side. Prismedia's image route matches.
- **Playback session bodies** Prismedia receives match real Jellyfin exactly:
  - `POST /Sessions/Playing` — `PlayMethod` (`DirectStream`), `PositionTicks`, `RunTimeTicks`,
    `MediaSourceId`, `ItemId`, `PlaySessionId`, `NowPlayingQueue[]`, `IsPaused`, `PlaylistIndex/Length`,
    `RepeatMode`, `CanSeek`, `IsMuted`.
  - `POST /Sessions/Playing/Progress` — adds `EventName` (`Pause`/`Unpause`) and `IsPaused`.
  - `POST /Sessions/Playing/Stopped` — `PlaySessionId`, `PositionTicks`, `Failed`, `ItemId`,
    `MediaSourceId`, `NowPlayingQueue[]`.
- Resume thresholds (resume 5–90%, ≥90% watched, <5% start-over) are reflected in the captured
  `UserData` (`PlayedPercentage` ≈ 19% → `Played:false`, resume position retained).

## Architectural direction — internal-first watch state, Jellyfin as a projection

Rather than building Jellyfin-specific shelves (`NextUp`, `Resume`) as one-off compatibility hacks,
model watch state as **first-class Prismedia dashboard concepts** and project them outward to the
Jellyfin endpoints. This makes the feature useful in Prismedia's own UI and gives the Jellyfin layer
a clean source.

Two internal sections, both derivable from the existing `PlaybackCapability`
(`PlayCount`, `PlayDurationSeconds`, `ResumeSeconds`, `LastPlayedAt`, `CompletedAt`):

- **Continue** (in-progress): entities with `ResumeSeconds > 0 && CompletedAt == null`, ordered by
  `LastPlayedAt` desc.
  - Projects to **`GET /UserItems/Resume`**.
- **Recently Watched** (completed): entities with `CompletedAt != null`, ordered by `CompletedAt`
  desc.
  - Feeds the derivation of **`GET /Shows/NextUp`**: for a completed episode, surface the next
    unwatched episode in the same series; in-progress episodes carry over from Continue.

Build the rich internal query once (with the parent-image data episodes need), then map it into the
Jellyfin DTO shape. The episode parent-image fields and `ImageBlurHashes` the Jellyfin layer needs
should be produced by this shared projection so both the Prismedia UI and Infuse get identical data.

## Prioritized work plan

### Done (this pass)
- **Episode parent-image fields** — `SeriesPrimaryImageTag`, `ParentLogoItemId/ImageTag`,
  `ParentBackdropItemId/ImageTags`, `ParentThumbItemId/ImageTag`, `IndexNumberEnd` added to the item
  DTO and populated from a shared parent-image context (series supplies logo/backdrop/primary; season
  supplies thumb, falling back to the series thumb).
- **`GET /Shows/NextUp`** — returns in-progress episodes (the internal Continue projection scoped to
  shows). Movies stay on the resume shelf.
- **Stop-404 endpoints** — `GET /Items/{id}/LocalTrailers` and `/SpecialFeatures` return `[]`;
  `GET /MediaSegments/{id}` returns the empty paged envelope. (Also registered `/MediaSegments` in the
  Jellyfin path allowlists in `PrismediaAuthentication` and `SpaDevProxy`, required for any new
  root-level Jellyfin prefix.)
- **MediaSource enrichment** — `ETag`, `VideoType`, `Bitrate`, `DefaultAudioStreamIndex`, and the
  `Supports*`/`Requires*`/`Ignore*`/`HasSegments`/`MediaAttachments`/`Formats` fields.
- **MediaStream** — added an Audio stream (channels/layout/sample rate) plus `RealFrameRate`,
  `AspectRatio`, and `IsExternal` for subtitles. `DefaultAudioStreamIndex` now resolves.
- **Small item fields + `UserData.ItemId`** — `VideoType`, `CanDelete`, `EnableMediaSourceDisplay`,
  `DisplayPreferencesId`, `LocalTrailerCount`/`SpecialFeatureCount` (0), and `UserData.ItemId`.
- Endpoint coverage tests in `Prismedia.Api.Tests/JellyfinCatalogExtrasEndpointTests.cs`.

### Deferred (need the worker/probe layer — separate workstream)
- **`ImageBlurHashes`** — DTO fields exist on the item and `People[]` (and always-present shape is
  ready), but real blurhashes require decoding each stored image and running the blurhash algorithm
  at scan/probe time, then caching by image tag. Left null (omitted) until that pipeline lands;
  absence only costs the blurred load-in placeholder, not function.
- **Full HDR / Dolby-Vision `MediaStream` metadata** (`VideoRange`, `ColorSpace`, `ColorTransfer`,
  `ColorPrimaries`, `DvProfile`/`DvLevel`/…, `Profile`, `Level`, `BitDepth`, `PixelFormat`). The probe
  already computes these (`ApplicationContractMapping.cs`, `HlsAssetService.cs`) but they are not
  surfaced on the API-facing `TechnicalCapability`. Threading them through is the next step; until
  then `VideoRange` is omitted rather than asserting an incorrect `SDR`.
- **UserData write endpoint** for favorite toggles (watched already works via `/UserPlayedItems`).
- **NextUp next-unwatched-after-completed** derivation (v1 surfaces in-progress episodes only).

### Verify
- Run a **Mode B capture** (proxy upstream = Prismedia `:8008`) and diff against
  `~/jf-ground-truth.json`.
