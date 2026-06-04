/**
 * Prismedia Plugin System — Type definitions.
 *
 * Defines the Prismedia-native plugin manifest, capabilities,
 * execution protocol, and normalized result types for all entity types.
 */

// ─── Plugin Manifest ───────────────────────────────────────────────

import type { ImageCandidate } from "@prismedia/contracts";

export type PluginRuntime = "python" | "typescript" | "stash-compat";

export interface PluginAuthField {
  key: string;
  label: string;
  required: boolean;
  url?: string;
}

export interface PrismediaPluginManifest {
  id: string;
  name: string;
  version: string;
  author?: string;
  description?: string;
  homepage?: string;
  isNsfw: boolean;
  tags?: string[];
  runtime: PluginRuntime;

  // Runtime-specific fields
  /** TypeScript: relative path to compiled entry (e.g. "dist/index.js") */
  entry?: string;
  /** Python: command + args (e.g. ["python3", "tvdb.py"]) */
  script?: string[];
  /** stash-compat: relative path to the stash .yml definition file */
  stashDefinition?: string;
  /** Python: sibling packages required (e.g. ["py_common"]) */
  requires?: string[];

  auth?: PluginAuthField[];
  capabilities: PluginCapabilities;
}

// ─── Plugin Capabilities ───────────────────────────────────────────

export interface PluginCapabilities {
  // Video (scene) identification
  videoByURL?: boolean;
  videoByFragment?: boolean;
  videoByName?: boolean;

  // Folder (series/season) identification
  folderByName?: boolean;
  folderByFragment?: boolean;
  /** Can map a resolved series/season to individual episode ↔ scene mappings */
  folderCascade?: boolean;

  // Gallery identification
  galleryByURL?: boolean;
  galleryByFragment?: boolean;

  // Book/comic/manga identification
  bookByURL?: boolean;
  bookByName?: boolean;
  bookByFragment?: boolean;
  comicByURL?: boolean;
  comicByName?: boolean;
  comicByFragment?: boolean;
  mangaByURL?: boolean;
  mangaByName?: boolean;
  mangaByFragment?: boolean;

  // Image identification
  imageByURL?: boolean;

  // Audio identification
  audioByURL?: boolean;
  audioByFragment?: boolean;
  audioLibraryByName?: boolean;

  // Performer identification
  performerByURL?: boolean;
  performerByFragment?: boolean;
  performerByName?: boolean;

  // Movies — Plan C
  movieByName?: boolean;
  movieByURL?: boolean;
  movieByFragment?: boolean;

  // Series (show-level lookup)
  seriesByName?: boolean;
  seriesByURL?: boolean;
  seriesByFragment?: boolean;

  // Series cascade (returns full season+episode tree)
  seriesCascade?: boolean;

  // Episode per-file lookup
  episodeByName?: boolean;
  episodeByFragment?: boolean;

  // Batch support
  supportsBatch?: boolean;
}

export const pluginCapabilityKeys: (keyof PluginCapabilities)[] = [
  "videoByURL",
  "videoByFragment",
  "videoByName",
  "folderByName",
  "folderByFragment",
  "folderCascade",
  "galleryByURL",
  "galleryByFragment",
  "bookByURL",
  "bookByName",
  "bookByFragment",
  "comicByURL",
  "comicByName",
  "comicByFragment",
  "mangaByURL",
  "mangaByName",
  "mangaByFragment",
  "imageByURL",
  "audioByURL",
  "audioByFragment",
  "audioLibraryByName",
  "performerByURL",
  "performerByFragment",
  "performerByName",
  "movieByName",
  "movieByURL",
  "movieByFragment",
  "seriesByName",
  "seriesByURL",
  "seriesByFragment",
  "seriesCascade",
  "episodeByName",
  "episodeByFragment",
  "supportsBatch",
];

/** Map Prismedia action names to Stash action names for the adapter. */
export const prismediaToStashActionMap: Record<string, string> = {
  videoByURL: "sceneByURL",
  videoByFragment: "sceneByFragment",
  videoByName: "sceneByName",
  galleryByURL: "galleryByURL",
  galleryByFragment: "galleryByFragment",
  bookByURL: "bookByURL",
  bookByName: "bookByName",
  bookByFragment: "bookByFragment",
  comicByURL: "comicByURL",
  comicByName: "comicByName",
  comicByFragment: "comicByFragment",
  mangaByURL: "mangaByURL",
  mangaByName: "mangaByName",
  mangaByFragment: "mangaByFragment",
  performerByURL: "performerByURL",
  performerByFragment: "performerByFragment",
  performerByName: "performerByName",
};

// ─── Plugin Execution Protocol ─────────────────────────────────────

/** Envelope sent to native Python/TS plugins via stdin. */
export interface PluginExecutionInput {
  prismedia_version: 1;
  action: string;
  auth: Record<string, string>;
  input?: PluginInput;
  batch?: BatchItem[];
}

export interface PluginInput {
  url?: string;
  title?: string;
  name?: string;
  date?: string;
  details?: string;
  oshash?: string;
  checksumMd5?: string;
  duration?: number;
  filePath?: string;
  /** For folder cascade: the external series ID to resolve episodes for. */
  externalId?: string;
  seasonNumber?: number;
}

export interface BatchItem {
  id: string;
  input: PluginInput;
}

/** Envelope returned by native plugins via stdout. */
export interface PluginExecutionOutput<T = unknown> {
  ok: boolean;
  result?: T;
  results?: Array<{ id: string; result: T | null }>;
  error?: string;
}

// ─── PrismediaPlugin Interface (for TypeScript plugins) ───────────────

export interface PrismediaPlugin {
  capabilities: PluginCapabilities;
  execute(
    action: string,
    input: PluginInput,
    auth: Record<string, string>,
  ): Promise<unknown>;
  executeBatch?(
    action: string,
    items: BatchItem[],
    auth: Record<string, string>,
  ): Promise<Array<{ id: string; result: unknown | null }>>;
}

// ─── Normalized Result Types ───────────────────────────────────────

export interface NormalizedVideoResult {
  title: string | null;
  date: string | null;
  details: string | null;
  urls: string[];
  studioName: string | null;
  performerNames: string[];
  tagNames: string[];
  imageUrl: string | null;
  episodeNumber: number | null;
  series: NormalizedSeriesRef | null;
  code: string | null;
  director: string | null;
}

export interface NormalizedSeriesRef {
  name: string;
  externalId?: string;
  season?: number;
  episode?: number;
}

export interface NormalizedFolderResult {
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

export interface EpisodeMapping {
  episodeNumber: number;
  seasonNumber: number;
  title: string | null;
  date: string | null;
  details: string | null;
}

export interface NormalizedGalleryResult {
  title: string | null;
  date: string | null;
  details: string | null;
  urls: string[];
  studioName: string | null;
  performerNames: string[];
  tagNames: string[];
  imageUrl: string | null;
  photographer: string | null;
  externalIds?: Record<string, string>;
  candidates?: NormalizedGalleryCandidate[];
  isNsfw?: boolean;
}

export interface NormalizedGalleryCandidate {
  externalIds: Record<string, string>;
  title: string;
  year?: number | null;
  overview?: string | null;
  posterUrl?: string | null;
  language?: string | null;
  contentRating?: string | null;
  source?: string | null;
  popularity?: number | null;
}

export interface NormalizedBookResult {
  title: string | null;
  date: string | null;
  details: string | null;
  urls: string[];
  studioName: string | null;
  performerNames: string[];
  tagNames: string[];
  imageUrl: string | null;
  chapterImageUrl?: string | null;
  chapterNumber?: number | null;
  imageCandidates?: ImageCandidate[];
  chapterImageCandidates?: ImageCandidate[];
  chapterImageByNumber?: Record<string, ImageCandidate>;
  volumeCovers?: NormalizedBookVolumeCover[];
  chapterVolumeByNumber?: Record<string, string>;
  chapterTitleByNumber?: Record<string, string>;
  externalIds?: Record<string, string>;
  candidates?: NormalizedBookCandidate[];
  isNsfw?: boolean;
}

export interface NormalizedBookVolumeCover extends ImageCandidate {
  volumeNumber: string;
  title?: string | null;
  externalIds?: Record<string, string>;
}

export interface NormalizedBookCandidate {
  externalIds: Record<string, string>;
  title: string;
  year?: number | null;
  overview?: string | null;
  posterUrl?: string | null;
  language?: string | null;
  contentRating?: string | null;
  source?: string | null;
  popularity?: number | null;
}

export interface NormalizedImageResult {
  title: string | null;
  date: string | null;
  details: string | null;
  urls: string[];
  tagNames: string[];
  imageUrl: string | null;
}

export interface NormalizedAudioTrackResult {
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

export interface NormalizedAudioLibraryResult {
  name: string | null;
  artist: string | null;
  details: string | null;
  date: string | null;
  imageUrl: string | null;
  urls: string[];
  tagNames: string[];
  trackCount?: number;
}

/** Union of all possible plugin result types. */
export type PluginResult =
  | NormalizedVideoResult
  | NormalizedFolderResult
  | NormalizedGalleryResult
  | NormalizedBookResult
  | NormalizedImageResult
  | NormalizedAudioTrackResult
  | NormalizedAudioLibraryResult;

// ─── Series Search (folder identification stage 1) ─────────────────

export interface SeriesCandidate {
  externalId: string;
  title: string;
  year?: string;
  network?: string;
  overview?: string;
  posterUrl?: string;
  seasonCount?: number;
  episodeCount?: number;
}

// ─── Unified Installed Plugin DTO ──────────────────────────────────

export interface InstalledPluginDto {
  id: string;
  pluginId: string;
  name: string;
  version: string;
  runtime: PluginRuntime;
  isNsfw: boolean;
  enabled: boolean;
  capabilities: Record<string, boolean>;
  sourceIndex: string | null;
  /** "ok" if all required auth keys are configured, "missing" if not, null if no auth required. */
  authStatus: "ok" | "missing" | null;
  authFields?: PluginAuthField[];
}

// ─── Community Index Entry ─────────────────────────────────────────

export interface PluginIndexEntry {
  id: string;
  name: string;
  version: string;
  date: string;
  path: string;
  sha256: string;
  runtime: PluginRuntime;
  isNsfw: boolean;
  capabilities: Record<string, boolean>;
  description?: string;
  author?: string;
  requires?: string[];
  installed?: boolean;
}
