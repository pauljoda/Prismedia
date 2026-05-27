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

export {
  BACKGROUND_WORKER_CONCURRENCY_MAX,
  BACKGROUND_WORKER_CONCURRENCY_MIN,
  jobKinds,
  jobRunRetention,
  jobStatuses,
  jobTriggerKinds,
  normalizeBackgroundWorkerConcurrency,
  queueDefinitions,
  resolveQueueWorkerConcurrency,
} from "./jobs";

export type {
  JobKind,
  JobStatus,
  JobTriggerKind,
  QueueName,
} from "./jobs";

export * from "./subtitles";
export * from "./library";
export * from "./playback";
export * from "./legacy-dtos";
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
