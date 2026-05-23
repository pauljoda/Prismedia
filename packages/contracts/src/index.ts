export { apiRoutes } from "./routes";


export const API_BASE_URL =
  typeof window !== "undefined"
    ? (process.env.PUBLIC_API_URL ?? "/api")
    : (process.env.API_URL ?? process.env.PUBLIC_API_URL ?? "http://localhost:8008/api");

export {
  canUseInlineVideoPreview,
  formatDuration,
  formatFileSize,
  getHlsRenditions,
  getResolutionLabel,
  HLS_RENDITION_PRESETS,
  HLS_RETRY_AFTER_SECONDS,
  isVideoImage,
  isVideoImageFormat,
  VIDEO_IMAGE_FORMATS,
  VIDEO_PREVIEW_MAX_FILE_SIZE_BYTES,
} from "./media";

export type {
  HlsPackageState,
  HlsRendition,
  HlsStatus,
} from "./media";

export type {
  PaginatedResponse,
  ErrorResponse,
  ListQuery,
  VideoListQuery,
  VideoSeriesListQuery,
  GalleryListQuery,
  PerformerListQuery,
  ImageListQuery,
  StudioListQuery,
  TagListQuery,
  AudioLibraryListQuery,
  AudioTrackListQuery,
  CollectionListQuery,
  CollectionItemListQuery,
  BulkUpdateResult,
} from "./queries";

export const queueDefinitions = [
  {
    name: "library-scan",
    label: "Library Scan",
    description: "Discovers videos (series, seasons, episodes, and movies) in configured media roots",
    concurrency: 1,
  },
  {
    name: "media-probe",
    label: "Media Probe",
    description: "Extracts technical metadata using ffprobe",
    concurrency: 1,
  },
  {
    name: "fingerprint",
    label: "Fingerprint",
    description: "Generates md5 and oshash fingerprints for videos",
    concurrency: 1,
  },
  {
    name: "preview",
    label: "Preview Build",
    description: "Builds video thumbnails, preview clips, and trickplay sprites",
    concurrency: 1,
  },
  {
    name: "metadata-import",
    label: "Metadata Import",
    description: "Coordinates provider imports and metadata application",
    concurrency: 1,
  },
  {
    name: "gallery-scan",
    label: "Gallery Scan",
    description: "Discovers image galleries in configured media roots",
    concurrency: 1,
  },
  {
    name: "book-scan",
    label: "Book Scan",
    description: "Discovers comic books in configured media roots",
    concurrency: 1,
  },
  {
    name: "book-page-thumbnail",
    label: "Book Page Thumbnail",
    description: "Generates thumbnails for comic book pages",
    concurrency: 1,
  },
  {
    name: "image-thumbnail",
    label: "Image Thumbnail",
    description: "Generates thumbnails and lightweight previews for images",
    concurrency: 1,
  },
  {
    name: "image-fingerprint",
    label: "Image Fingerprint",
    description: "Computes md5 and oshash fingerprints for images",
    concurrency: 1,
  },
  {
    name: "audio-scan",
    label: "Audio Scan",
    description: "Discovers audio tracks in configured media roots",
    concurrency: 1,
  },
  {
    name: "audio-probe",
    label: "Audio Probe",
    description: "Extracts technical metadata and embedded tags from audio files",
    concurrency: 1,
  },
  {
    name: "audio-fingerprint",
    label: "Audio Fingerprint",
    description: "Computes md5 and oshash fingerprints for audio tracks",
    concurrency: 1,
  },
  {
    name: "audio-waveform",
    label: "Audio Waveform",
    description: "Generates waveform peaks data for audio playback visualization",
    concurrency: 1,
  },
  {
    name: "library-maintenance",
    label: "Library maintenance",
    description: "Moves video-derived assets between cache and media-adjacent storage",
    concurrency: 1,
  },
  {
    name: "extract-subtitles",
    label: "Extract Subtitles",
    description: "Extracts embedded subtitle tracks from video files as WebVTT",
    concurrency: 1,
  },
  {
    name: "collection-refresh",
    label: "Collection Refresh",
    description: "Re-evaluates dynamic collection rules and updates membership",
    concurrency: 1,
  },
  {
    name: "plugin-batch-identify",
    label: "Plugin Batch Identify",
    description: "Batch metadata identification via Prismedia plugins",
    concurrency: 1,
  },
] as const;

export type SubtitleSource = "sidecar" | "upload" | "embedded";

/**
 * The original on-disk format. `vtt` means the server only has a WebVTT
 * representation. `ass`/`ssa` means the original Advanced SubStation file is
 * preserved alongside the VTT fallback, and the player can render it with
 * full libass fidelity (fonts, positioning, colors, karaoke, animations) via
 * the `sourceUrl`.
 */
export type SubtitleSourceFormat = "vtt" | "srt" | "ass" | "ssa";

export interface VideoSubtitleTrackDto {
  id: string;
  videoId: string;
  language: string;
  label: string | null;
  format: "vtt";
  source: SubtitleSource;
  sourceFormat: SubtitleSourceFormat;
  isDefault: boolean;
  url: string;
  /** Present when the server has preserved the original file (e.g. .ass). */
  sourceUrl: string | null;
  createdAt: string;
}

export interface SubtitleCueDto {
  start: number;
  end: number;
  text: string;
}

/** BullMQ jobs per queue; default 1. Higher values increase CPU, disk, and memory use. */
export const BACKGROUND_WORKER_CONCURRENCY_MIN = 1;
export const BACKGROUND_WORKER_CONCURRENCY_MAX = 32;

export function normalizeBackgroundWorkerConcurrency(raw: unknown): number {
  const n = typeof raw === "number" ? raw : Number(raw);
  if (!Number.isFinite(n)) {
    return BACKGROUND_WORKER_CONCURRENCY_MIN;
  }
  const floor = Math.floor(n);
  return Math.min(
    BACKGROUND_WORKER_CONCURRENCY_MAX,
    Math.max(BACKGROUND_WORKER_CONCURRENCY_MIN, floor)
  );
}

/** Effective BullMQ concurrency for a queue: definition base × normalized user setting. */
export function resolveQueueWorkerConcurrency(
  definitionConcurrency: number,
  backgroundWorkerConcurrency: unknown
): number {
  const base = Math.max(1, Math.floor(definitionConcurrency));
  const k = normalizeBackgroundWorkerConcurrency(backgroundWorkerConcurrency);
  return base * k;
}

export type QueueName = (typeof queueDefinitions)[number]["name"];

export const jobRunRetention = {
  completed: 40,
  dismissed: 40,
} as const;

export const jobStatuses = [
  "waiting",
  "active",
  "completed",
  "failed",
  "dismissed",
  "delayed",
  "paused",
] as const;

export type JobStatus = (typeof jobStatuses)[number];

export const jobTriggerKinds = [
  "manual",
  "schedule",
  "library-scan",
  "gallery-scan",
  "book-scan",
  "audio-scan",
  "system",
] as const;

export type JobTriggerKind = (typeof jobTriggerKinds)[number];

export const jobKinds = ["standard", "force-rebuild"] as const;

export type JobKind = (typeof jobKinds)[number];

/** Lightweight library root summary used by upload target pickers. */
export interface LibraryRootSummaryDto {
  id: string;
  path: string;
  label: string;
  enabled: boolean;
  scanVideos: boolean;
  scanImages: boolean;
  scanAudio: boolean;
  scanBooks: boolean;
}

export interface UploadVideoResponseDto {
  id: string;
  title: string;
  filePath: string;
  libraryRootId: string;
}

export interface UploadImageResponseDto {
  id: string;
  title: string;
  filePath: string;
  galleryId: string;
}

export interface UploadAudioTrackResponseDto {
  id: string;
  title: string;
  filePath: string;
  libraryId: string;
}

export interface LibraryRootDto {
  id: string;
  path: string;
  label: string;
  enabled: boolean;
  recursive: boolean;
  scanVideos: boolean;
  scanImages: boolean;
  scanAudio: boolean;
  scanBooks: boolean;
  isNsfw: boolean;
  lastScannedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface LibraryBrowseEntryDto {
  name: string;
  path: string;
}

export interface LibraryBrowseDto {
  path: string;
  parentPath: string | null;
  directories: LibraryBrowseEntryDto[];
}

export interface LibrarySettingsDto {
  id: string;
  autoScanEnabled: boolean;
  scanIntervalMinutes: number;
  autoGenerateMetadata: boolean;
  autoGenerateFingerprints: boolean;
  /** When true, the worker computes a Stash-compatible perceptual hash during fingerprint jobs. CPU-heavy; required for contributing fingerprints to StashDB/ThePornDB. */
  generatePhash: boolean;
  autoGeneratePreview: boolean;
  generateTrickplay: boolean;
  trickplayIntervalSeconds: number;
  previewClipDurationSeconds: number;
  thumbnailQuality: number;
  trickplayQuality: number;
  /** Parallel jobs per queue in the worker process (1–32). */
  backgroundWorkerConcurrency: number;
  nsfwLanAutoEnable: boolean;
  /** Video derivatives (thumb, preview, sprite, trickplay) in cache dir vs next to media. */
  metadataStorageDedicated: boolean;
  /** When true, player auto-enables a subtitle track on load if a matching language is available. */
  subtitlesAutoEnable: boolean;
  /** Comma-separated BCP-47 / ISO 639 preference list (e.g. "en,eng,en-US"). First match wins. */
  subtitlesPreferredLanguages: string;
  /** Visual style applied to the caption overlay. */
  subtitleStyle: SubtitleDisplayStyle;
  /** Font scale multiplier (1.0 = default). Clamped to [0.5, 3.0]. */
  subtitleFontScale: number;
  /** Vertical position as a 0–100 percentage from the top of the video frame. */
  subtitlePositionPercent: number;
  /** Overall caption layer opacity (0.2–1.0). Applied to the full overlay including text, box, and shadows. */
  subtitleOpacity: number;
  /** Default playback mode the video player boots into on each new source. */
  defaultPlaybackMode: PlaybackMode;
  /** When true, the video player shows remote playback / casting controls. */
  showCastControls: boolean;
  /** Encoder profile used when the adaptive HLS pipeline generates new segments. */
  hlsTranscoderProfile: HlsTranscoderProfile;
  /** Executable name or absolute path used to launch ffmpeg for adaptive HLS. */
  hlsFfmpegPath: string;
  /** Linux render device used by the VA-API HLS transcoder profile. */
  hlsVaapiDevice: string;
  createdAt: string;
  updatedAt: string;
}

export const playbackModes = ["direct", "hls"] as const;
export type PlaybackMode = (typeof playbackModes)[number];

export function normalizePlaybackMode(value: unknown): PlaybackMode {
  return typeof value === "string" && (playbackModes as readonly string[]).includes(value)
    ? (value as PlaybackMode)
    : "direct";
}

export const hlsTranscoderProfiles = [
  "Software",
  "Auto",
  "VideoToolbox",
  "Vaapi",
  "Nvenc",
  "Qsv",
] as const;
export type HlsTranscoderProfile = (typeof hlsTranscoderProfiles)[number];

export function normalizeHlsTranscoderProfile(value: unknown): HlsTranscoderProfile {
  return typeof value === "string" && (hlsTranscoderProfiles as readonly string[]).includes(value)
    ? (value as HlsTranscoderProfile)
    : "Software";
}

export const subtitleDisplayStyles = ["stylized", "classic", "outline"] as const;
export type SubtitleDisplayStyle = (typeof subtitleDisplayStyles)[number];

export interface SubtitleAppearance {
  style: SubtitleDisplayStyle;
  fontScale: number;
  positionPercent: number;
  /** Overall caption layer opacity (0.2–1.0). */
  opacity: number;
}

export const defaultSubtitleAppearance: SubtitleAppearance = {
  style: "stylized",
  fontScale: 1,
  positionPercent: 88,
  opacity: 1,
};

export interface StorageStatsDto {
  thumbnailsBytes: number;
  previewsBytes: number;
  trickplayBytes: number;
  totalBytes: number;
}

/** Tag embedded on videos, images, galleries, and performers in list/detail payloads. */
export interface TagEmbedDto {
  id: string;
  name: string;
  isNsfw: boolean;
}

// ─── Video Series DTOs ───────────────────────────────────────────

export interface VideoSeriesListItemDto {
  id: string;
  title: string;
  customName: string | null;
  displayTitle: string;
  folderPath: string;
  relativePath: string;
  parentId: string | null;
  depth: number;
  organized: boolean;
  isNsfw: boolean;
  coverImagePath: string | null;
  backdropImagePath: string | null;
  studioId: string | null;
  studioName: string | null;
  rating: number | null;
  date: string | null;
  directVideoCount: number;
  totalVideoCount: number;
  visibleSfwVideoCount: number;
  containsNsfwDescendants: boolean;
  childSeasonCount: number;
  previewThumbnailPaths: string[];
  libraryRootId: string;
  libraryRootLabel: string;
  createdAt: string;
  updatedAt: string;
}

export interface VideoSeriesBreadcrumbDto {
  id: string;
  title: string;
  displayTitle: string;
}

/**
 * Season summary projected onto the series detail response. Case A
 * series (flat, episodes only in season 0) have exactly one entry.
 * Case B series have numbered seasons (and optionally a Specials
 * row with seasonNumber = 0).
 */
export interface VideoSeriesSeasonDto {
  id: string;
  seasonNumber: number;
  title: string | null;
  posterPath: string | null;
  episodeCount: number;
  previewThumbnailPath: string | null;
}

export interface VideoSeriesDetailDto extends VideoSeriesListItemDto {
  details: string | null;
  urls: string[];
  externalSeriesId: string | null;
  studio: { id: string; name: string } | null;
  performers: {
    id: string;
    name: string;
    gender: string | null;
    imagePath: string | null;
    isNsfw: boolean;
    character?: string | null;
  }[];
  tags: TagEmbedDto[];
  breadcrumbs: VideoSeriesBreadcrumbDto[];
  children: VideoSeriesListItemDto[];
  /** Seasons under this series. Empty for movie-style series. */
  seasons: VideoSeriesSeasonDto[];
  /**
   * `"flat"` when the UI should render the series as a single
   * uninterrupted episode list (Case A — only season 0 populated).
   * `"seasons"` when the UI should render season headers with
   * episodes nested under them (Case B — any numbered season).
   */
  renderingMode: "flat" | "seasons";
}

export interface VideoSeriesPatchDto {
  isNsfw?: boolean;
  organized?: boolean;
  customName?: string | null;
  details?: string | null;
  studioName?: string | null;
  performerNames?: string[];
  tagNames?: string[];
  rating?: number | null;
  date?: string | null;
}

// ─── Gallery DTOs ────────────────────────────────────────────────

export type GalleryType = "folder" | "zip" | "virtual";

export interface GalleryListItemDto {
  id: string;
  title: string;
  galleryType: GalleryType;
  isComic: boolean;
  readCompleted: boolean;
  coverImagePath: string | null;
  previewImagePaths: string[];
  coverAspectRatio: number | null;
  childCount: number;
  imageCount: number;
  rating: number | null;
  organized: boolean;
  isNsfw: boolean;
  date: string | null;
  studioId: string | null;
  studioName: string | null;
  performers: { id: string; name: string }[];
  tags: TagEmbedDto[];
  parentId: string | null;
  createdAt: string;
}

export interface GalleryDetailDto {
  id: string;
  title: string;
  details: string | null;
  galleryType: GalleryType;
  isComic: boolean;
  date: string | null;
  rating: number | null;
  organized: boolean;
  isNsfw: boolean;
  photographer: string | null;
  folderPath: string | null;
  zipFilePath: string | null;
  parentId: string | null;
  coverImageId: string | null;
  coverImagePath: string | null;
  imageCount: number;
  studio: { id: string; name: string; url: string | null } | null;
  performers: { id: string; name: string; gender: string | null; imagePath: string | null }[];
  tags: TagEmbedDto[];
  chapters: GalleryChapterDto[];
  images: ImageListItemDto[];
  imageTotal: number;
  imageLimit: number;
  imageOffset: number;
  children: { id: string; title: string; imageCount: number; coverImagePath: string | null; previewImagePaths: string[]; coverAspectRatio: number | null; childCount: number; isNsfw: boolean; isComic: boolean }[];
  createdAt: string;
  updatedAt: string;
}

export interface GalleryChapterDto {
  id: string;
  galleryId: string;
  title: string;
  imageIndex: number;
}

export interface GalleryStatsDto {
  totalGalleries: number;
  totalImages: number;
  recentCount: number;
}

// ─── Book DTOs ───────────────────────────────────────────────────

export type BookType = "comic";

export interface BookListItemDto {
  id: string;
  bookType: BookType;
  title: string;
  details: string | null;
  coverImagePath: string | null;
  previewImagePaths: string[];
  pageCount: number;
  chapterCount: number;
  rating: number | null;
  organized: boolean;
  isNsfw: boolean;
  date: string | null;
  studioId: string | null;
  studioName: string | null;
  performers: { id: string; name: string }[];
  tags: TagEmbedDto[];
  readCompleted: boolean;
  progress: BookProgressDto | null;
  createdAt: string;
  updatedAt: string;
}

export interface BookDetailDto {
  id: string;
  bookType: BookType;
  title: string;
  details: string | null;
  coverImagePath: string | null;
  previewImagePaths: string[];
  pageCount: number;
  chapterCount: number;
  rating: number | null;
  organized: boolean;
  isNsfw: boolean;
  date: string | null;
  studioId: string | null;
  studioName: string | null;
  tags: TagEmbedDto[];
  readCompleted: boolean;
  progress: BookProgressDto | null;
  createdAt: string;
  updatedAt: string;
  folderPath: string | null;
  relativePath: string;
  urls: string[];
  studio: { id: string; name: string; url: string | null } | null;
  performers: { id: string; name: string; gender: string | null; imagePath: string | null; isNsfw: boolean }[];
  volumes: BookVolumeDto[];
  chapters: BookChapterDto[];
}

export interface BookVolumeDto {
  id: string;
  bookId: string;
  volumeNumber: number | null;
  title: string;
  folderPath: string | null;
  relativePath: string | null;
  coverImagePath: string | null;
  hasCustomCover: boolean;
  pageCount: number;
  chapterCount: number;
  externalIds: Record<string, string>;
  chapters: BookChapterDto[];
}

export interface BookChapterDto {
  id: string;
  bookId: string;
  volumeId: string | null;
  title: string;
  chapterNumber: number;
  archivePath: string;
  relativePath: string;
  pageCount: number;
  coverPageId: string | null;
  coverImagePath: string | null;
  hasCustomCover: boolean;
  pages: BookPageDto[];
}

export interface BookPageDto {
  id: string;
  bookId: string;
  chapterId: string;
  title: string;
  width: number | null;
  height: number | null;
  format: string | null;
  thumbnailPath: string | null;
  fullPath: string;
  sortOrder: number;
}

export interface BookProgressDto {
  bookId: string;
  chapterId: string | null;
  pageIndex: number;
  pageCount: number;
  readerMode: "paged" | "webtoon";
  completedAt: string | null;
  updatedAt: string;
}

export interface BookProgressPatchDto {
  chapterId?: string | null;
  pageIndex?: number;
  pageCount?: number;
  readerMode?: "paged" | "webtoon";
  completedAt?: string | null;
}

// ─── Image DTOs ──────────────────────────────────────────────────

export interface ImageListItemDto {
  id: string;
  title: string;
  date: string | null;
  rating: number | null;
  organized: boolean;
  isNsfw: boolean;
  width: number | null;
  height: number | null;
  format: string | null;
  isVideo: boolean;
  fileSize: number | null;
  thumbnailPath: string | null;
  previewPath: string | null;
  fullPath: string | null;
  galleryId: string | null;
  sortOrder: number;
  studioId: string | null;
  performers: { id: string; name: string }[];
  tags: TagEmbedDto[];
  createdAt: string;
}

export interface ImageDetailDto extends ImageListItemDto {
  details: string | null;
  checksumMd5: string | null;
  oshash: string | null;
  filePath: string;
  studio: { id: string; name: string } | null;
  updatedAt: string;
}

export interface GalleryImagesPageDto {
  images: ImageListItemDto[];
  total: number;
  limit: number;
  offset: number;
}

export interface ImageUpdateDto {
  title?: string;
  details?: string | null;
  date?: string | null;
  rating?: number | null;
  organized?: boolean;
  isNsfw?: boolean;
  studioName?: string | null;
  performerNames?: string[];
  tagNames?: string[];
}

export interface ImageBulkUpdateDto {
  ids: string[];
  patch: {
    rating?: number | null;
    organized?: boolean;
    tagNames?: string[];
    galleryId?: string | null;
  };
}

export interface QueueSummaryDto {
  name: QueueName;
  label: string;
  description: string;
  status: "idle" | "active" | "warning";
  concurrency: number;
  active: number;
  waiting: number;
  delayed: number;
  backlog: number;
  completed: number;
  failed: number;
}

export interface JobRunDto {
  id: string;
  queueName: QueueName;
  queueLabel: string;
  status: JobStatus;
  targetType: string | null;
  targetId: string | null;
  targetLabel: string | null;
  triggeredBy: JobTriggerKind | null;
  triggerLabel: string | null;
  jobKind: JobKind | null;
  progress: number;
  attempts: number;
  error: string | null;
  startedAt: string | null;
  finishedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

// ─── Scraper DTOs ────────────────────────────────────────────────

export interface ScraperPackageDto {
  id: string;
  packageId: string;
  name: string;
  version: string;
  installPath: string;
  sha256: string | null;
  capabilities: Record<string, boolean> | null;
  enabled: boolean;
  isNsfw: boolean;
  pluginType: string;
  createdAt: string;
  updatedAt: string;
}

export interface CommunityIndexEntryDto {
  id: string;
  name: string;
  version: string;
  date: string;
  path: string;
  sha256: string;
  requires?: string[];
  installed?: boolean;
  installedVersion?: string;
}

export interface ScrapeResultDto {
  id: string;
  entityType: string;
  entityId: string | null;
  scraperPackageId: string | null;
  stashBoxEndpointId: string | null;
  pluginPackageId: string | null;
  action: string;
  matchType: string | null;
  status: "pending" | "accepted" | "rejected";
  rawResult: unknown;
  proposedTitle: string | null;
  proposedDate: string | null;
  proposedDetails: string | null;
  proposedUrl: string | null;
  proposedUrls: string[] | null;
  proposedStudioName: string | null;
  proposedPerformerNames: string[] | null;
  proposedTagNames: string[] | null;
  proposedImageUrl: string | null;
  proposedEpisodeNumber: number | null;
  proposedSeriesResult: unknown | null;
  proposedAudioResult: unknown | null;
  /**
   * Typed payload for the new cascade review flow (Plan C /
   * Plan D). Plugins that implement `seriesCascade`, `movieByName`,
   * `episodeBy*` etc. write their discriminated
   * `{ kind, movie | series | episode }` result here; the cascade
   * review drawer discriminates on the shape of this field.
   */
  proposedResult: unknown | null;
  /**
   * When a plugin seek returns ancillary child scrape-results
   * alongside a parent (e.g. per-episode results linked to a series
   * cascade), they are threaded through this parent id.
   */
  cascadeParentId: string | null;
  appliedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ScraperCapabilities {
  sceneByURL: boolean;
  sceneByFragment: boolean;
  sceneByName: boolean;
  performerByURL: boolean;
  performerByName: boolean;
  performerByFragment: boolean;
}

// ─── Plugin DTOs ───────────────────────────────────────────────

export interface PluginPackageDto {
  id: string;
  pluginId: string;
  name: string;
  version: string;
  runtime: string;
  installPath: string;
  sha256: string | null;
  isNsfw: boolean;
  capabilities: Record<string, boolean> | null;
  enabled: boolean;
  sourceIndex: string | null;
  authStatus: "ok" | "missing" | null;
  authFields?: Array<{
    key: string;
    label: string;
    required: boolean;
    url?: string;
  }>;
  createdAt: string;
  updatedAt: string;
}

export interface PluginIndexEntryDto {
  id: string;
  name: string;
  version: string;
  date: string;
  path: string;
  sha256: string;
  runtime: string;
  isNsfw: boolean;
  capabilities: Record<string, boolean>;
  description?: string;
  author?: string;
  requires?: string[];
  installed?: boolean;
  installedVersion?: string;
}

// ─── Stash-Box DTOs ─────────────────────────────────────────────

export interface StashBoxEndpointDto {
  id: string;
  name: string;
  endpoint: string;
  /** Masked — only last 4 chars shown */
  apiKeyPreview: string;
  enabled: boolean;
  /**
   * Always true. Every StashBox-protocol endpoint (StashDB, FansDB,
   * ThePornDB, MetadataAPI, etc.) is treated as NSFW so the SFW mode
   * filter hides them from the identify / bulk-scrape provider list.
   */
  isNsfw: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface StashBoxEndpointCreateDto {
  name: string;
  endpoint: string;
  apiKey: string;
}

export type MetadataProviderType = "scraper" | "stashbox";

export interface MetadataProviderDto {
  id: string;
  name: string;
  type: MetadataProviderType;
  enabled: boolean;
  capabilities: Record<string, boolean>;
}

// ─── Performer DTOs ─────────────────────────────────────────────

export interface PerformerListItemDto {
  id: string;
  name: string;
  disambiguation: string | null;
  gender: string | null;
  imagePath: string | null;
  favorite: boolean;
  rating: number | null;
  isNsfw: boolean;
  videoCount: number;
  /** Linked galleries plus standalone images (SFW mode excludes NSFW entities). */
  imageAppearanceCount: number;
  seriesCount: number;
  galleryCount: number;
  imageCount: number;
  audioLibraryCount: number;
  audioTrackCount: number;
  appearanceCount: number;
  country: string | null;
  createdAt: string;
}

export interface PerformerDetailDto {
  id: string;
  name: string;
  disambiguation: string | null;
  aliases: string | null;
  gender: string | null;
  birthdate: string | null;
  country: string | null;
  ethnicity: string | null;
  eyeColor: string | null;
  hairColor: string | null;
  height: number | null;
  weight: number | null;
  measurements: string | null;
  tattoos: string | null;
  piercings: string | null;
  careerStart: number | null;
  careerEnd: number | null;
  details: string | null;
  imageUrl: string | null;
  imagePath: string | null;
  favorite: boolean;
  rating: number | null;
  isNsfw: boolean;
  videoCount: number;
  seriesCount: number;
  galleryCount: number;
  imageCount: number;
  imageAppearanceCount: number;
  audioLibraryCount: number;
  audioTrackCount: number;
  appearanceCount: number;
  knownFor: PerformerKnownForDto[];
  tags: TagEmbedDto[];
  createdAt: string;
  updatedAt: string;
}

export type PerformerKnownForSourceType = "movie" | "series" | "episode";

export interface PerformerKnownForDto {
  sourceType: PerformerKnownForSourceType;
  sourceId: string;
  sourceTitle: string;
  character: string | null;
  thumbnailPath: string | null;
  cardThumbnailPath: string | null;
  seriesId: string | null;
  seriesTitle: string | null;
  seasonNumber: number | null;
  episodeNumber: number | null;
}

export interface PerformerUpdateDto {
  name?: string;
  disambiguation?: string | null;
  aliases?: string | null;
  gender?: string | null;
  birthdate?: string | null;
  country?: string | null;
  ethnicity?: string | null;
  eyeColor?: string | null;
  hairColor?: string | null;
  height?: number | null;
  weight?: number | null;
  measurements?: string | null;
  tattoos?: string | null;
  piercings?: string | null;
  careerStart?: number | null;
  careerEnd?: number | null;
  details?: string | null;
  imageUrl?: string | null;
  favorite?: boolean;
  rating?: number | null;
  isNsfw?: boolean;
  tagNames?: string[];
}

export interface NormalizedPerformerResult {
  name: string | null;
  disambiguation: string | null;
  gender: string | null;
  birthdate: string | null;
  country: string | null;
  ethnicity: string | null;
  eyeColor: string | null;
  hairColor: string | null;
  height: string | null;
  weight: string | null;
  measurements: string | null;
  tattoos: string | null;
  piercings: string | null;
  aliases: string | null;
  details: string | null;
  imageUrl: string | null;
  imageUrls: string[];
  tagNames: string[];
}

// ─── Search DTOs ────────────────────────────────────────────────

export type EntityKind = "video" | "video-series" | "performer" | "studio" | "tag" | "gallery" | "image" | "book" | "audio-library" | "audio-track";

export interface SearchResultItem {
  id: string;
  kind: EntityKind;
  title: string;
  subtitle: string | null;
  imagePath: string | null;
  href: string;
  rating: number | null;
  score: number;
  meta: Record<string, string | number | boolean | string[] | null>;
}

export interface SearchResultGroup {
  kind: EntityKind;
  label: string;
  items: SearchResultItem[];
  total: number;
  hasMore: boolean;
}

export interface SearchResponseDto {
  query: string;
  groups: SearchResultGroup[];
  durationMs: number;
}

// ─── Jobs DTOs ───────────────────────────────────────────────────

export interface JobsDashboardDto {
  queues: QueueSummaryDto[];
  activeJobs: JobRunDto[];
  failedJobs: JobRunDto[];
  completedJobs: JobRunDto[];
  recentJobs: JobRunDto[];
  lastScanAt: string | null;
  schedule: {
    enabled: boolean;
    intervalMinutes: number;
  };
}

// ─── Audio Library DTOs ────────────────────────────────────────

export interface AudioLibraryListItemDto {
  id: string;
  title: string;
  coverImagePath: string | null;
  iconPath: string | null;
  trackCount: number;
  rating: number | null;
  organized: boolean;
  isNsfw: boolean;
  date: string | null;
  studioId: string | null;
  studioName: string | null;
  performers: { id: string; name: string }[];
  tags: TagEmbedDto[];
  parentId: string | null;
  createdAt: string;
}

export interface AudioLibraryDetailDto {
  id: string;
  title: string;
  details: string | null;
  date: string | null;
  rating: number | null;
  organized: boolean;
  isNsfw: boolean;
  folderPath: string | null;
  parentId: string | null;
  coverImagePath: string | null;
  iconPath: string | null;
  trackCount: number;
  totalDuration: number | null;
  studio: { id: string; name: string; url: string | null } | null;
  performers: { id: string; name: string; gender: string | null; imagePath: string | null }[];
  tags: TagEmbedDto[];
  tracks: AudioTrackListItemDto[];
  trackTotal: number;
  trackLimit: number;
  trackOffset: number;
  children: { id: string; title: string; trackCount: number; coverImagePath: string | null; iconPath: string | null; isNsfw: boolean }[];
  createdAt: string;
  updatedAt: string;
}

/** PATCH /audio-libraries/:id */
export interface AudioLibraryPatchDto {
  title?: string;
  details?: string | null;
  date?: string | null;
  rating?: number | null;
  organized?: boolean;
  isNsfw?: boolean;
  studioName?: string | null;
  performerNames?: string[];
  tagNames?: string[];
}

/** PATCH /audio-tracks/:id */
export interface AudioTrackPatchDto {
  title?: string;
  details?: string | null;
  date?: string | null;
  rating?: number | null;
  organized?: boolean;
  isNsfw?: boolean;
  studioName?: string | null;
  performerNames?: string[];
  tagNames?: string[];
}

export interface AudioLibraryStatsDto {
  totalLibraries: number;
  totalTracks: number;
  totalDuration: number;
  recentCount: number;
}

// ─── Audio Track DTOs ──────────────────────────────────────────

export interface AudioTrackListItemDto {
  id: string;
  title: string;
  date: string | null;
  rating: number | null;
  organized: boolean;
  isNsfw: boolean;
  duration: number | null;
  bitRate: number | null;
  sampleRate: number | null;
  channels: number | null;
  codec: string | null;
  fileSize: number | null;
  embeddedArtist: string | null;
  embeddedAlbum: string | null;
  trackNumber: number | null;
  waveformPath: string | null;
  libraryId: string | null;
  sortOrder: number;
  studioId: string | null;
  performers: { id: string; name: string }[];
  tags: TagEmbedDto[];
  playCount: number;
  lastPlayedAt: string | null;
  createdAt: string;
}

export interface AudioTrackDetailDto extends AudioTrackListItemDto {
  details: string | null;
  checksumMd5: string | null;
  oshash: string | null;
  filePath: string;
  container: string | null;
  resumeTime: number;
  playDuration: number;
  studio: { id: string; name: string } | null;
  markers: AudioTrackMarkerDto[];
  updatedAt: string;
}

export interface AudioTrackMarkerDto {
  id: string;
  trackId: string;
  title: string;
  seconds: number;
  endSeconds: number | null;
}

// ─── Collection Types ──────────────────────────────────────────

export type CollectionMode = "manual" | "dynamic" | "hybrid";
export type CollectionCoverMode = "mosaic" | "custom" | "item";
export type CollectionEntityType = "video" | "gallery" | "image" | "book" | "audio-track";
export type CollectionItemSource = "manual" | "dynamic";

// ─── Collection Rule Tree ──────────────────────────────────────

export type CollectionOperator =
  | "equals"
  | "not_equals"
  | "contains"
  | "not_contains"
  | "greater_than"
  | "less_than"
  | "greater_equal"
  | "less_equal"
  | "between"
  | "in"
  | "not_in"
  | "is_null"
  | "is_not_null"
  | "is_true"
  | "is_false";

export type CollectionConditionValue =
  | string
  | number
  | boolean
  | string[]
  | [number, number]
  | null;

export interface CollectionRuleCondition {
  type: "condition";
  /** Entity types this condition applies to. Empty array = all types. */
  entityTypes: CollectionEntityType[];
  field: string;
  operator: CollectionOperator;
  value: CollectionConditionValue;
}

export interface CollectionRuleGroup {
  type: "group";
  operator: "and" | "or" | "not";
  children: (CollectionRuleCondition | CollectionRuleGroup)[];
}

export type CollectionRuleNode = CollectionRuleCondition | CollectionRuleGroup;

export type CollectionRuleFieldType =
  | "text"
  | "number"
  | "boolean"
  | "date"
  | "relation"
  | "enum";

export interface CollectionRuleFieldDef {
  field: string;
  label: string;
  fieldType: CollectionRuleFieldType;
  /** Entity types this field is available for. Empty = all types. */
  entityTypes: CollectionEntityType[];
  /** For enum fields, the available values. */
  enumValues?: string[];
  /** Which operators are valid for this field type. */
  operators: CollectionOperator[];
}

export const COLLECTION_RULE_FIELDS: CollectionRuleFieldDef[] = [
  // Universal fields (apply to all entity types)
  { field: "title", label: "Title", fieldType: "text", entityTypes: [], operators: ["contains", "not_contains", "equals", "not_equals"] },
  { field: "rating", label: "Rating", fieldType: "number", entityTypes: [], operators: ["equals", "not_equals", "greater_than", "less_than", "greater_equal", "less_equal", "between", "is_null", "is_not_null"] },
  { field: "date", label: "Date", fieldType: "date", entityTypes: [], operators: ["equals", "not_equals", "greater_than", "less_than", "between", "is_null", "is_not_null"] },
  { field: "organized", label: "Organized", fieldType: "boolean", entityTypes: [], operators: ["is_true", "is_false"] },
  { field: "isNsfw", label: "NSFW", fieldType: "boolean", entityTypes: [], operators: ["is_true", "is_false"] },
  { field: "tags", label: "Tags", fieldType: "relation", entityTypes: [], operators: ["in", "not_in"] },
  { field: "performers", label: "Performers", fieldType: "relation", entityTypes: [], operators: ["in", "not_in"] },
  { field: "studio", label: "Studio", fieldType: "relation", entityTypes: [], operators: ["in", "not_in", "is_null", "is_not_null"] },
  { field: "createdAt", label: "Added Date", fieldType: "date", entityTypes: [], operators: ["greater_than", "less_than", "between"] },
  { field: "fileSize", label: "File Size", fieldType: "number", entityTypes: ["video", "image", "audio-track"], operators: ["greater_than", "less_than", "between"] },

  // Video-specific
  { field: "duration", label: "Duration", fieldType: "number", entityTypes: ["video", "audio-track"], operators: ["greater_than", "less_than", "between", "is_null", "is_not_null"] },
  { field: "resolution", label: "Resolution", fieldType: "enum", entityTypes: ["video"], operators: ["in", "not_in"], enumValues: ["4K", "1080p", "720p", "480p"] },
  { field: "codec", label: "Codec", fieldType: "text", entityTypes: ["video"], operators: ["equals", "not_equals", "in", "not_in"] },
  { field: "interactive", label: "Interactive", fieldType: "boolean", entityTypes: ["video"], operators: ["is_true", "is_false"] },
  { field: "playCount", label: "Play Count", fieldType: "number", entityTypes: ["video", "audio-track"], operators: ["equals", "greater_than", "less_than", "greater_equal", "less_equal", "between"] },
  { field: "videoSeriesId", label: "Series", fieldType: "relation", entityTypes: ["video"], operators: ["equals", "in", "not_in"] },

  // Gallery-specific
  { field: "galleryType", label: "Gallery Type", fieldType: "enum", entityTypes: ["gallery"], operators: ["equals", "not_equals", "in"], enumValues: ["folder", "zip", "virtual"] },
  { field: "imageCount", label: "Image Count", fieldType: "number", entityTypes: ["gallery"], operators: ["greater_than", "less_than", "greater_equal", "less_equal", "between"] },

  // Image-specific
  { field: "width", label: "Width", fieldType: "number", entityTypes: ["image"], operators: ["greater_than", "less_than", "between"] },
  { field: "height", label: "Height", fieldType: "number", entityTypes: ["image"], operators: ["greater_than", "less_than", "between"] },
  { field: "format", label: "Format", fieldType: "text", entityTypes: ["image"], operators: ["equals", "not_equals", "in", "not_in"] },

  // Audio-specific
  { field: "bitRate", label: "Bit Rate", fieldType: "number", entityTypes: ["audio-track"], operators: ["greater_than", "less_than", "between"] },
  { field: "channels", label: "Channels", fieldType: "number", entityTypes: ["audio-track"], operators: ["equals", "greater_than", "less_than"] },
];

// ─── Collection DTOs ───────────────────────────────────────────

export interface CollectionListItemDto {
  id: string;
  name: string;
  description: string | null;
  mode: CollectionMode;
  itemCount: number;
  coverMode: CollectionCoverMode;
  coverImagePath: string | null;
  slideshowDurationSeconds: number;
  slideshowAutoAdvance: boolean;
  isNsfw: boolean;
  /** Breakdown of items by entity type. */
  typeCounts: Record<CollectionEntityType, number>;
  lastRefreshedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CollectionDetailDto extends CollectionListItemDto {
  ruleTree: CollectionRuleGroup | null;
  coverItemId: string | null;
  coverItemType: CollectionEntityType | null;
}

export interface CollectionItemDto {
  id: string;
  collectionId: string;
  entityType: CollectionEntityType;
  entityId: string;
  source: CollectionItemSource;
  sortOrder: number;
  addedAt: string;
  /**
   * Polymorphic entity embed — exactly one of these is populated based on
   * entityType. The shape matches the respective list-item endpoint response
   * (VideoListItem, GalleryListItemDto, ImageListItemDto, AudioTrackListItemDto).
   */
  entity: Record<string, unknown> | null;
}

export interface CollectionCreateDto {
  name: string;
  description?: string;
  mode?: CollectionMode;
  ruleTree?: CollectionRuleGroup;
  slideshowDurationSeconds?: number;
  slideshowAutoAdvance?: boolean;
  isNsfw?: boolean;
}

export interface CollectionPatchDto {
  name?: string;
  description?: string | null;
  mode?: CollectionMode;
  ruleTree?: CollectionRuleGroup | null;
  coverMode?: CollectionCoverMode;
  coverItemId?: string | null;
  coverItemType?: CollectionEntityType | null;
  slideshowDurationSeconds?: number;
  slideshowAutoAdvance?: boolean;
  isNsfw?: boolean;
}

export interface CollectionAddItemsDto {
  items: { entityType: CollectionEntityType; entityId: string }[];
}

export interface CollectionRemoveItemsDto {
  itemIds: string[];
}

export interface CollectionReorderDto {
  /** Ordered list of item IDs — position in array becomes sortOrder. */
  itemIds: string[];
}

export interface CollectionRulePreviewDto {
  total: number;
  byType: Record<CollectionEntityType, number>;
  sample: CollectionItemDto[];
}

export interface PlaylistSessionDto {
  collectionId: string | null;
  collectionName: string;
  items: CollectionItemDto[];
  playOrder: number[];
  orderPosition: number;
  shuffle: boolean;
  loop: boolean;
  slideshowDurationSeconds: number;
  updatedAt: string;
}

export interface PlaylistSessionWriteDto {
  collectionId: string | null;
  collectionName: string;
  items: CollectionItemDto[];
  playOrder: number[];
  orderPosition: number;
  shuffle: boolean;
  loop: boolean;
  slideshowDurationSeconds: number;
}

// ─── Scene DTOs ─────────────────────────────────────────────────

export interface VideoListItemDto {
  id: string;
  title: string;
  details: string | null;
  date: string | null;
  rating: number | null;
  organized: boolean;
  isNsfw: boolean;
  duration: number | null;
  durationFormatted: string | null;
  resolution: string | null;
  width: number | null;
  height: number | null;
  codec: string | null;
  container: string | null;
  fileSize: number | null;
  fileSizeFormatted: string | null;
  filePath: string | null;
  hasVideo: boolean;
  streamUrl: string | null;
  directStreamUrl: string | null;
  thumbnailPath: string | null;
  cardThumbnailPath: string | null;
  spritePath: string | null;
  trickplayVttPath: string | null;
  playCount: number;
  orgasmCount: number;
  studioId: string | null;
  videoSeriesId: string | null;
  videoSeriesTitle: string | null;
  /** Season number for episode rows (null for movies). */
  seasonNumber: number | null;
  /** Episode number within the season (null for movies). */
  episodeNumber: number | null;
  hasSubtitles: boolean;
  performers: {
    id: string;
    name: string;
    gender?: string | null;
    imagePath?: string | null;
    favorite?: boolean;
    isNsfw?: boolean;
    /** Role name from the join table (e.g. "Ron Trosper"). */
    character?: string | null;
    /** Whether the displayed role came from the direct entity row or inherited series cast. */
    roleSource?: "episode" | "series" | "movie" | null;
  }[];
  tags: TagEmbedDto[];
  createdAt: string;
  updatedAt: string;
}

export interface VideoCardListItemDto {
  id: string;
  title: string;
  rating: number | null;
  organized: boolean;
  isNsfw: boolean;
  duration: number | null;
  durationFormatted: string | null;
  resolution: string | null;
  codec: string | null;
  fileSizeFormatted: string | null;
  thumbnailPath: string | null;
  cardThumbnailPath: string | null;
  spritePath: string | null;
  trickplayVttPath: string | null;
  playCount: number;
  videoSeriesId: string | null;
  seasonNumber: number | null;
  episodeNumber: number | null;
  hasSubtitles: boolean;
  performers: {
    id: string;
    name: string;
    imagePath?: string | null;
    isNsfw?: boolean;
  }[];
  tags: TagEmbedDto[];
  createdAt: string;
  updatedAt: string;
}

export interface VideoDetailDto extends VideoListItemDto {
  interactive: boolean;
  frameRate: number | null;
  bitRate: number | null;
  previewPath: string | null;
  playDuration: number | null;
  resumeTime: number | null;
  lastPlayedAt: string | null;
  url: string | null;
  urls: string[];
  /**
   * Episode placement fields, only populated when `entityKind ===
   * "video_episode"`. `seasonNumber` is 0 for Specials / flat-series
   * layouts. `absoluteEpisodeNumber` is the cumulative count across
   * all seasons (honored by the filename parser when the scheme only
   * carries a single number).
   */
  seasonNumber: number | null;
  episodeNumber: number | null;
  absoluteEpisodeNumber: number | null;
  /**
   * Discriminator so the UI can tell whether a given row is backed
   * by `video_episodes` or `video_movies` without a second round-trip.
   * Drives behavior like the Identify button on the detail page (which
   * calls into the matching `/video/{movies|episodes}/:id/accept-scrape`
   * endpoint).
   */
  entityKind: "video_episode" | "video_movie";
  studio: { id: string; name: string; url: string | null } | null;
  markers: VideoMarkerDto[];
  subtitleTracks: VideoSubtitleTrackDto[];
  /**
   * Whether the video has computed fingerprints used for fingerprint-
   * based identify lookups (Stash-Box). `phash` is the perceptual hash
   * computed by the phash worker; `oshash` and `checksumMd5` are file
   * hashes computed during scan. The Identify flyout uses these to
   * surface helper text when a Stash-Box provider would have nothing
   * to query against.
   */
  fingerprints: {
    hasPhash: boolean;
    hasOshash: boolean;
    hasChecksumMd5: boolean;
  };
}

export interface VideoMarkerDto {
  id: string;
  title: string;
  seconds: number;
  endSeconds: number | null;
}

export interface VideoStatsDto {
  totalScenes: number;
  totalDuration: number;
  totalDurationFormatted: string;
  totalSize: number;
  totalSizeFormatted: string;
  totalPlays: number;
  recentCount: number;
}

// ─── Studio DTOs ────────────────────────────────────────────────

export interface StudioListItemDto {
  id: string;
  name: string;
  url: string | null;
  imageUrl: string | null;
  imagePath: string | null;
  favorite: boolean;
  rating: number | null;
  isNsfw: boolean;
  videoCount: number;
  /** Galleries + images with this studio (SFW mode excludes NSFW entities). */
  imageAppearanceCount: number;
  audioLibraryCount: number;
}

export interface StudioParentRefDto {
  id: string;
  name: string;
  imagePath: string | null;
  imageUrl: string | null;
}

export interface StudioChildRefDto {
  id: string;
  name: string;
  imagePath: string | null;
  imageUrl: string | null;
  videoCount: number;
}

export interface StudioDetailDto {
  id: string;
  name: string;
  description: string | null;
  aliases: string | null;
  url: string | null;
  parentId: string | null;
  parent: StudioParentRefDto | null;
  childStudios: StudioChildRefDto[];
  imageUrl: string | null;
  imagePath: string | null;
  favorite: boolean;
  rating: number | null;
  isNsfw: boolean;
  videoCount: number;
  createdAt: string;
  updatedAt: string;
}

// ─── Tag DTOs ───────────────────────────────────────────────────

export interface TagListItemDto {
  id: string;
  name: string;
  videoCount: number;
  galleryCount?: number;
  imageCount: number;
  audioTrackCount?: number;
  imagePath: string | null;
  favorite: boolean;
  rating: number | null;
  isNsfw: boolean;
}

export interface TagDetailDto {
  id: string;
  name: string;
  description: string | null;
  aliases: string | null;
  parentId: string | null;
  imageUrl: string | null;
  imagePath: string | null;
  favorite: boolean;
  rating: number | null;
  isNsfw: boolean;
  ignoreAutoTag: boolean;
  videoCount: number;
  createdAt: string;
  updatedAt: string;
}

// ─── Scrape / StashBox Result DTOs ──────────────────────────────

export interface NormalizedSceneScrapeResultDto {
  title: string | null;
  date: string | null;
  details: string | null;
  url: string | null;
  studioName: string | null;
  performerNames: string[];
  tagNames: string[];
  imageUrl: string | null;
}

export interface NormalizedStudioScrapeResultDto {
  name: string | null;
  url: string | null;
  imageUrl: string | null;
  parentName: string | null;
}

export interface NormalizedTagScrapeResultDto {
  name: string | null;
  description: string | null;
  aliases: string | null;
}

export interface StashIdEntryDto {
  id: string;
  entityType: string;
  entityId: string;
  endpointId: string;
  endpointName: string;
  stashId: string;
  createdAt: string;
}

export interface StashBoxStudioResultDto {
  id: string;
  name: string;
  aliases: string[];
  urls: { url: string; type: string }[];
  parent: { id: string; name: string } | null;
  images: { id: string; url: string; width: number; height: number }[];
}

export interface StashBoxTagResultDto {
  id: string;
  name: string;
  description: string | null;
  aliases: string[];
  category: { id: string; name: string; description: string | null } | null;
}

export * from "./external-ids";

export type {
  ImageCandidate,
  NormalizedCastMember,
  NormalizedMovieResult,
  NormalizedSeriesResult,
  NormalizedSeriesCandidate,
  NormalizedSeasonResult,
  NormalizedEpisodeResult,
} from "./normalized-video";
