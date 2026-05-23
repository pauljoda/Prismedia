---
sidebar_position: 3
title: Capabilities
description: Every action a plugin can declare, what input it gets, and what shape it returns.
---

# Capabilities

A plugin's `capabilities` map declares which actions it implements. Each capability key corresponds to a single `action` string the engine may dispatch.

The full type lives at `packages/plugins/src/types.ts:46-94`.

```ts
export interface PluginCapabilities {
  // Video (scene)
  videoByURL?: boolean;
  videoByFragment?: boolean;
  videoByName?: boolean;

  // Folder (series / season)
  folderByName?: boolean;
  folderByFragment?: boolean;
  folderCascade?: boolean;

  // Gallery
  galleryByURL?: boolean;
  galleryByFragment?: boolean;

  // Image
  imageByURL?: boolean;

  // Audio
  audioByURL?: boolean;
  audioByFragment?: boolean;
  audioLibraryByName?: boolean;

  // Performer
  performerByURL?: boolean;
  performerByFragment?: boolean;
  performerByName?: boolean;

  // Movie
  movieByName?: boolean;
  movieByURL?: boolean;
  movieByFragment?: boolean;

  // Series
  seriesByName?: boolean;
  seriesByURL?: boolean;
  seriesByFragment?: boolean;
  seriesCascade?: boolean;

  // Episode
  episodeByName?: boolean;
  episodeByFragment?: boolean;

  // Batch
  supportsBatch?: boolean;
}
```

Declare only what your plugin can actually answer. The engine uses the map to filter the provider picker, so an over-declared capability becomes a dead-end the user has to discover by trying it.

## The capability matrix

### Video (scene-style) lookups

| Action | Input fields used | Returns | When it fires |
| --- | --- | --- | --- |
| `videoByURL` | `url` | `NormalizedVideoResult` | Identify on a single video, user pastes a URL. |
| `videoByName` | `title`, `name`, `date` | `NormalizedVideoResult` | Identify on a single video, name-based fallback. |
| `videoByFragment` | `oshash`, `checksumMd5`, `phash`, `duration` | `NormalizedVideoResult` | Fingerprint-based lookup (StashBox-style). |

### Folder / series identification

| Action | Input fields used | Returns | When it fires |
| --- | --- | --- | --- |
| `folderByName` | `name`, `title` | `NormalizedFolderResult` | A series or season is identified by its folder name. |
| `folderByFragment` | (heuristic) | `NormalizedFolderResult` | Folder structure heuristics. |
| `folderCascade` | `externalId` (from prior lookup), `seasonNumber` | `NormalizedFolderResult` with `episodeMap` | Map a series's child files to specific episodes. |

The cascade pattern: a `folderByName` returns a `seriesExternalId`, then `folderCascade` is invoked with that ID + each season number to pull down the episode list.

### Gallery & image

| Action | Input | Returns |
| --- | --- | --- |
| `galleryByURL` | `url` | `NormalizedGalleryResult` |
| `galleryByFragment` | (provider-specific) | `NormalizedGalleryResult` |
| `imageByURL` | `url` | `NormalizedImageResult` |

### Audio

| Action | Input | Returns |
| --- | --- | --- |
| `audioByURL` | `url` | `NormalizedAudioTrackResult` |
| `audioByFragment` | (provider-specific) | `NormalizedAudioTrackResult` |
| `audioLibraryByName` | `name` | `NormalizedAudioLibraryResult` |

### Performer

| Action | Input | Returns |
| --- | --- | --- |
| `performerByURL` | `url` | Performer result (Stash-compat shape). |
| `performerByName` | `name` | Performer result. |
| `performerByFragment` | `name` | Performer result. |

### Movie

| Action | Input | Returns |
| --- | --- | --- |
| `movieByName` | `name`, `title` | `NormalizedMovieResult` |
| `movieByURL` | `url` | `NormalizedMovieResult` |
| `movieByFragment` | (provider-specific) | `NormalizedMovieResult` |

### Series (TMDB-style cascade)

| Action | Input | Returns |
| --- | --- | --- |
| `seriesByName` | `name`, `title` | `NormalizedSeriesResult` or `{ candidates: NormalizedSeriesCandidate[] }` |
| `seriesByURL` | `url` | `NormalizedSeriesResult` |
| `seriesByFragment` | (provider-specific) | `NormalizedSeriesResult` |
| `seriesCascade` | `externalId` (series ID from prior lookup) | `NormalizedSeriesResult` with `seasons[].episodes[]` populated |

`seriesByName` may return multiple candidate matches when a query is ambiguous (e.g. a remake exists). The cascade UI shows the candidate picker, the user chooses, then `seriesCascade` re-runs with the chosen `externalId`.

### Episode (rare, usually cascaded)

| Action | Input | Returns |
| --- | --- | --- |
| `episodeByName` | `name`, `title` | `NormalizedEpisodeResult` |
| `episodeByFragment` | (provider-specific) | `NormalizedEpisodeResult` |

In most cases episodes come back as part of a `seriesCascade`. The standalone episode actions exist for providers that can identify a single episode without the series context.

### Batch

| Capability | Meaning |
| --- | --- |
| `supportsBatch` | The plugin can handle multiple items in one execution envelope. The engine sends `batch: BatchItem[]` instead of `input` and expects `results: { id, result }[]`. |

Batch is an optimization. Plugins that support it can deduplicate API calls or share rate limits across items. If you don't, leave `supportsBatch: false` and the engine will dispatch one item at a time.

## Result types

These are the shapes plugins return. All are defined in `packages/plugins/src/types.ts`.

### `NormalizedVideoResult`

```ts
interface NormalizedVideoResult {
  title: string | null;
  date: string | null;
  details: string | null;
  urls: string[];
  studioName: string | null;
  performerNames: string[];
  tagNames: string[];
  imageUrl: string | null;        // single poster/cover URL
  episodeNumber: number | null;
  series: NormalizedSeriesRef | null;
  code: string | null;            // scene code / external ID
  director: string | null;
}

interface NormalizedSeriesRef {
  name: string;
  externalId?: string;
  season?: number;
  episode?: number;
}
```

### `NormalizedFolderResult`

```ts
interface NormalizedFolderResult {
  name: string | null;
  details: string | null;
  date: string | null;
  imageUrl: string | null;
  backdropUrl: string | null;
  studioName: string | null;
  tagNames: string[];
  urls: string[];
  seriesExternalId?: string;
  seasonNumber?: number;
  totalEpisodes?: number;
  episodeMap?: Record<string, EpisodeMapping>;
}

interface EpisodeMapping {
  episodeNumber: number;
  seasonNumber: number;
  title: string | null;
  date: string | null;
  details: string | null;
}
```

`episodeMap` is a lookup keyed by episode identifier (typically the source filename). It's how `folderCascade` returns child-episode metadata in one shot.

### `NormalizedGalleryResult`

```ts
interface NormalizedGalleryResult {
  title: string | null;
  date: string | null;
  details: string | null;
  urls: string[];
  studioName: string | null;
  performerNames: string[];
  tagNames: string[];
  imageUrl: string | null;
  photographer: string | null;
}
```

### `NormalizedImageResult`

```ts
interface NormalizedImageResult {
  title: string | null;
  date: string | null;
  details: string | null;
  urls: string[];
  tagNames: string[];
  imageUrl: string | null;
}
```

### `NormalizedAudioTrackResult`

```ts
interface NormalizedAudioTrackResult {
  title: string | null;
  artist: string | null;
  album: string | null;
  trackNumber: number | null;
  date: string | null;
  details: string | null;
  imageUrl: string | null;
  urls: string[];
  tagNames: string[];
}
```

### `NormalizedAudioLibraryResult`

```ts
interface NormalizedAudioLibraryResult {
  name: string | null;
  artist: string | null;
  details: string | null;
  date: string | null;
  imageUrl: string | null;
  urls: string[];
  tagNames: string[];
  trackCount?: number;
}
```

### Cascade types: movies, series, seasons, episodes

For TMDB-style cascade flows the discriminated types live in `@prismedia/contracts`:

```ts
interface NormalizedMovieResult {
  title: string;
  originalTitle?: string;
  overview?: string;
  tagline?: string;
  releaseDate?: string;
  runtime?: number;
  genres: string[];
  studioName?: string;
  cast: NormalizedCastMember[];
  posterCandidates: ImageCandidate[];
  backdropCandidates: ImageCandidate[];
  logoCandidates: ImageCandidate[];
  externalIds: Record<string, string>;
  rating?: number;
  contentRating?: string;
}

interface NormalizedSeriesResult {
  title: string;
  originalTitle?: string;
  overview?: string;
  status?: 'returning' | 'ended';
  genres: string[];
  studioName?: string;
  cast: NormalizedCastMember[];
  posterCandidates: ImageCandidate[];
  backdropCandidates: ImageCandidate[];
  logoCandidates: ImageCandidate[];
  externalIds: Record<string, string>;
  seasons: NormalizedSeasonResult[];
  candidates?: NormalizedSeriesCandidate[];   // disambiguation
  rating?: number;
  contentRating?: string;
}

interface NormalizedSeasonResult {
  seasonNumber: number;
  title?: string;
  overview?: string;
  airDate?: string;
  posterCandidates: ImageCandidate[];
  externalIds: Record<string, string>;
  episodes: NormalizedEpisodeResult[];
}

interface NormalizedEpisodeResult {
  seasonNumber: number;
  episodeNumber: number;
  absoluteEpisodeNumber?: number;
  title?: string;
  overview?: string;
  airDate?: string;
  runtime?: number;
  stillCandidates: ImageCandidate[];
  guestStars: NormalizedCastMember[];
  externalIds: Record<string, string>;
  matched?: boolean;
  localFilePath?: string;
}

interface ImageCandidate {
  url: string;            // required; HTTP(S) or data:image/
  language?: string;
  width?: number;
  height?: number;
  aspectRatio?: number;
  rank?: number;
  source?: string;
}

interface NormalizedCastMember {
  name: string;
  character?: string;
  order?: number;
  profileUrl?: string;
}
```

The `*Candidates` arrays let the user pick the poster/backdrop they want from multiple options. Surface as many as you have — the cascade drawer renders all of them.

## Image semantics

- `imageUrl` (singular, on the simpler results) → downloaded to `/data/cache/metadata/` on Accept. Must be HTTP(S) or `data:image/` (inline base64).
- `posterCandidates` / `backdropCandidates` / `logoCandidates` / `stillCandidates` → arrays of candidates with metadata. The user picks one in the cascade drawer; the chosen URL is downloaded.
- Plugins **return URLs**, not file paths. Local paths (`imagePath`) are written by the application after download.

## Nested entity creation

When the user accepts a result, the .NET backend walks the tree and creates missing entities by name:

1. **Performers** — created by name; **not** deduplicated against existing.
2. **Tags** — created by name; **deduplicated case-insensitively**.
3. **Studios** — created by name; **not** deduplicated.
4. **Images** — chosen poster/backdrop/still URLs downloaded to local cache, paths linked to the entity.

If you don't want a performer to be created, omit it from `performerNames`. If you want to be sure a performer matches an existing one, return the canonical spelling — name matching is exact (case-sensitive for performers, case-insensitive for tags).

## A worked example: TMDB series cascade

User clicks **Identify** on a series:

1. Engine asks TMDB plugin: `seriesByName({ name: "The Wire" })`.
2. TMDB returns `NormalizedSeriesResult` with multiple `candidates` (HBO original, BBC docu, etc.).
3. Cascade drawer shows the candidate picker. User clicks "The Wire (2002)".
4. Engine asks TMDB: `seriesCascade({ externalId: "1438" })`.
5. TMDB returns `NormalizedSeriesResult` with `seasons[].episodes[]` filled in for every season.
6. Cascade drawer renders: series header, per-season sections, per-episode rows.
7. User picks posters / backdrops, ticks per-field checkboxes, hits **Apply cascade**.
8. Engine writes `video_series`, `video_seasons`, `video_episodes`, links performers/tags/studios, downloads chosen images, and marks every applied row as `accepted` in `scrape_results`.

That's it. The plugin's only job was steps 1–5 — return the right shape.
