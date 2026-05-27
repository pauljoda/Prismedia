import {
  backfillFingerprints as backfillFingerprintsRequest,
  browseLibraryPath as browseLibraryPathRequest,
  cancelJobRun as cancelJobRunRequest,
  cancelJobs as cancelJobsRequest,
  clearJobFailures as clearJobFailuresRequest,
  createFileFolder as createFileFolderRequest,
  createEntityMarker as createEntityMarkerRequest,
  createJob as createJobRequest,
  createLibraryRoot as createLibraryRootRequest,
  deleteFile as deleteFileRequest,
  deleteEntityMarker as deleteEntityMarkerRequest,
  deleteJellyfinUserPlayedItem,
  deleteLibraryRoot as deleteLibraryRootRequest,
  getFileDetail,
  getGetFileContentUrl,
  excludeFile as excludeFileRequest,
  listEntities,
  listFileChildren,
  listFileRoots,
  listJobs,
  listVideoSeries,
  listVideos,
  moveFile as moveFileRequest,
  postJellyfinSessionPing,
  postJellyfinSessionPlaying,
  postJellyfinSessionProgress as postJellyfinSessionProgressRequest,
  postJellyfinSessionStopped,
  postJellyfinUserPlayedItem,
  renameFile as renameFileRequest,
  rebuildPreviews as rebuildPreviewsRequest,
  removeFileExclusion as removeFileExclusionRequest,
  rescanFileRoot as rescanFileRootRequest,
  updateEntityMarker as updateEntityMarkerRequest,
  updateEntityPlayback as updateEntityPlaybackRequest,
  getSetting as getSettingRequest,
  getSettings,
  resetSetting as resetSettingRequest,
  updateSetting as updateSettingRequest,
  updateSettings as updateSettingsRequest,
  uploadFiles as uploadFilesRequest,
  getVideoSeries,
  getVideo,
  getImage,
  getGallery,
  getBook,
  getAudioLibrary,
  getAudioTrack,
  getPerson,
  getStudio,
  getTag,
  getCollection,
  getEntity,
  getEntityThumbnails,
  getLibraryConfig,
  listPeople,
  listStudios,
  listTags,
  listCollections,
  listAudioLibraries,
  listAudioTracks,
  listBooks,
  listGalleries,
  listImages,
  getVideoSeason,
  updateLibraryRoot as updateLibraryRootRequest,
} from "./generated/prismedia";
import type {
  AudioLibraryDetail as GeneratedAudioLibraryDetail,
  AudioTrackDetail as GeneratedAudioTrackDetail,
  BookDetail as GeneratedBookDetail,
  CollectionDetail as GeneratedCollectionDetail,
  EntityCapability as GeneratedEntityCapability,
  EntityCard as GeneratedEntityCard,
  EntityGroup as GeneratedEntityGroup,
  EntityListResponse as GeneratedEntityListResponse,
  EntityThumbnail as GeneratedEntityThumbnail,
  EntityThumbnailBatchResponse as GeneratedEntityThumbnailBatchResponse,
  FileChildrenResponse as GeneratedFileChildrenResponse,
  FileCreateFolderRequest as GeneratedFileCreateFolderRequest,
  FileDetail as GeneratedFileDetail,
  FileEntry as GeneratedFileEntry,
  FileExclusionRequest as GeneratedFileExclusionRequest,
  FileMoveRequest as GeneratedFileMoveRequest,
  FileOperationResponse as GeneratedFileOperationResponse,
  FileRenameRequest as GeneratedFileRenameRequest,
  FileRescanRequest as GeneratedFileRescanRequest,
  FileRoot as GeneratedFileRoot,
  FileRootsResponse as GeneratedFileRootsResponse,
  GalleryDetail as GeneratedGalleryDetail,
  ImageDetail as GeneratedImageDetail,
  JobListResponse as GeneratedJobListResponse,
  JobRun as GeneratedJobRun,
  LibraryBrowseResponse as GeneratedLibraryBrowseResponse,
  LibraryConfigResponse as GeneratedLibraryConfigResponse,
  LibraryRoot as GeneratedLibraryRoot,
  ListEntitiesParams,
  PersonDetail as GeneratedPersonDetail,
  PlaybackSessionRequest as GeneratedPlaybackSessionRequest,
  PlaybackUpdateRequest as GeneratedPlaybackUpdateRequest,
  SettingConstraints as GeneratedSettingConstraints,
  SettingDescriptor as GeneratedSettingDescriptor,
  SettingsCatalogResponse as GeneratedSettingsCatalogResponse,
  SettingsGroup as GeneratedSettingsGroup,
  SettingsValuesResponse as GeneratedSettingsValuesResponse,
  StudioDetail as GeneratedStudioDetail,
  TagDetail as GeneratedTagDetail,
  VideoDetail as GeneratedVideoDetail,
  VideoSeriesDetail as GeneratedVideoSeriesDetail,
  VideoSeasonDetail as GeneratedVideoSeasonDetail,
} from "./generated/model";
import { requestInit, unwrapGenerated, type RequestOptions as GeneratedRequestOptions } from "$lib/api/generated-response";
import { fetchApi, jellyfinApiPath, apiPath } from "./orval-fetch";
import type { EntityFileRoleCode } from "$lib/entities/entity-codes";
export {
  clearEntityImageAsset,
  updateEntityFlags,
  updateEntityMetadata,
  updateEntityRating,
  uploadEntityImageAsset,
} from "$lib/api/entity-mutations";

export type EntityCapability = GeneratedEntityCapability;
export type EntityCard = GeneratedEntityThumbnail;
export type EntityDetailCard = GeneratedEntityCard;
export type EntityCardFull = GeneratedEntityCard;
export type EntityChildGroup = GeneratedEntityGroup;
export type EntityRelationshipGroup = GeneratedEntityGroup;
export type EntityThumbnail = GeneratedEntityThumbnail;
export type EntityListResponse = GeneratedEntityListResponse;
export type VideoListResponse = GeneratedEntityListResponse;
export type VideoDetail = GeneratedVideoDetail;
export type VideoSeriesListResponse = GeneratedEntityListResponse;
export type VideoSeriesDetail = GeneratedVideoSeriesDetail;
export type VideoSeasonDetail = GeneratedVideoSeasonDetail;
export type JobRun = GeneratedJobRun & {
  targetKind?: string | null;
  targetId?: string | null;
  targetLabel?: string | null;
};
export type JobListResponse = GeneratedJobListResponse;
export interface JobCreateResponse {
  job: JobRun;
}
export interface JobCancelResponse {
  cancelled: number;
}
export interface JobFailureClearResponse {
  cleared: number;
}
export interface BulkJobResponse {
  enqueued: number;
  skipped: number;
}
export interface WorkerHealthResponse {
  status: "online" | "offline" | string;
  workerId: string | null;
  lastSeenAt: string | null;
  staleAfterSeconds: number;
}
export type ImageDetail = GeneratedImageDetail;
export type GalleryDetail = GeneratedGalleryDetail;
export type BookDetail = GeneratedBookDetail;
export type AudioLibraryDetail = GeneratedAudioLibraryDetail;
export type AudioTrackDetail = GeneratedAudioTrackDetail;
export type PersonDetail = GeneratedPersonDetail;
export type StudioDetail = GeneratedStudioDetail;
export type TagDetail = GeneratedTagDetail;
export type CollectionDetail = GeneratedCollectionDetail;
export type CollectionListResponse = GeneratedEntityListResponse;
export type TaxonomyListResponse = GeneratedEntityListResponse;
export type SettingValue = boolean | number | string | string[];
export interface SettingConstraints {
  min?: number | null;
  max?: number | null;
  step?: number | null;
  minItems?: number | null;
  maxItems?: number | null;
}
export type SettingDescriptor = Omit<
  GeneratedSettingDescriptor,
  "value" | "defaultValue" | "order" | "constraints"
> & {
  value: SettingValue;
  defaultValue: SettingValue;
  order: number;
  constraints: SettingConstraints | null;
};
export type SettingsGroup = Omit<GeneratedSettingsGroup, "settings" | "order"> & {
  order: number;
  settings: SettingDescriptor[];
};
export interface SettingsCatalogResponse {
  groups: SettingsGroup[];
}
export type SettingsResponse = SettingsCatalogResponse;
export interface SettingsValuesResponse {
  values: Record<string, SettingValue>;
}
export type MediaListResponse = GeneratedEntityListResponse;
export interface EntityReference {
  id: string;
  kind: string;
  title: string;
  thumbnailUrl?: string | null;
}
export interface LibrarySettings {
  visibilityDefaultMode: "off" | "show";
  nsfwLanAutoEnable: boolean;
  autoScanEnabled: boolean;
  scanIntervalMinutes: number;
  autoGenerateMetadata: boolean;
  autoGenerateFingerprints: boolean;
  generatePhash: boolean;
  autoGeneratePreview: boolean;
  generateTrickplay: boolean;
  metadataStorageDedicated: boolean;
  trickplayIntervalSeconds: number;
  previewClipDurationSeconds: number;
  thumbnailQuality: string;
  trickplayQuality: string;
  backgroundWorkerConcurrency: number;
  defaultPlaybackMode: "direct" | "hls";
  showCastControls: boolean;
  audioPreferredLanguages: string;
  subtitlesAutoEnable: boolean;
  subtitlesPreferredLanguages: string;
  subtitleStyle: string;
  subtitleFontScale: number;
  subtitlePositionPercent: number;
  subtitleOpacity: number;
  hlsTranscoderProfile: string;
  hlsFfmpegPath: string;
  hlsVaapiDevice: string;
}
export type LibraryRoot = GeneratedLibraryRoot;
export type LibraryBrowse = GeneratedLibraryBrowseResponse;
export type FileRoot = GeneratedFileRoot;
export type FileEntry = GeneratedFileEntry;
export type FileDetail = GeneratedFileDetail & {
  directoryFileCount?: number | null;
  directoryTotalSizeBytes?: number | null;
};
export type FileChildrenResponse = GeneratedFileChildrenResponse;
export type FileOperationResponse = GeneratedFileOperationResponse;
type EntityThumbnailBatchResponse = GeneratedEntityThumbnailBatchResponse;
type FileCreateFolderRequest = GeneratedFileCreateFolderRequest;
type FileExclusionRequest = GeneratedFileExclusionRequest;
type FileMoveRequest = GeneratedFileMoveRequest;
type FileRenameRequest = GeneratedFileRenameRequest;
type FileRescanRequest = GeneratedFileRescanRequest;
type FileRootsResponse = GeneratedFileRootsResponse;
type PlaybackSessionRequest = GeneratedPlaybackSessionRequest;
type PlaybackUpdateRequest = GeneratedPlaybackUpdateRequest;
export interface LibraryConfigResponse {
  settings: SettingsCatalogResponse;
  roots: LibraryRoot[];
}

export interface FileUploadItem {
  file: File;
  relativePath: string;
}

export interface JellyfinPlaybackInfoRequest {
  UserId?: string | null;
  StartTimeTicks?: number | null;
  AudioStreamIndex?: number | null;
  SubtitleStreamIndex?: number | null;
  MaxStreamingBitrate?: number | null;
  EnableDirectPlay?: boolean | null;
  EnableDirectStream?: boolean | null;
  EnableTranscoding?: boolean | null;
  MediaSourceId?: string | null;
  PlaySessionId?: string | null;
  SupportedVideoRangeTypes?: string[] | null;
}

export interface JellyfinMediaStreamInfo {
  Index: number;
  Type: string;
  Codec?: string | null;
  Language?: string | null;
  DisplayTitle?: string | null;
  Width?: number | null;
  Height?: number | null;
  AverageFrameRate?: number | null;
  BitRate?: number | null;
  SampleRate?: number | null;
  Channels?: number | null;
  IsDefault?: boolean | null;
  IsForced?: boolean | null;
  VideoRange?: string | null;
  VideoRangeType?: string | null;
  PixelFormat?: string | null;
  BitDepth?: number | null;
  ColorRange?: string | null;
  ColorSpace?: string | null;
  ColorTransfer?: string | null;
  ColorPrimaries?: string | null;
  DvProfile?: number | null;
  DvLevel?: number | null;
  RpuPresentFlag?: boolean | null;
  ElPresentFlag?: boolean | null;
  BlPresentFlag?: boolean | null;
  DvBlSignalCompatibilityId?: number | null;
  Hdr10PlusPresentFlag?: boolean | null;
}

export interface JellyfinTranscodingInfo {
  Container: string;
  VideoCodec: string;
  AudioCodec: string;
  Protocol: string;
  IsVideoDirect: boolean;
  IsAudioDirect: boolean;
}

export interface JellyfinMediaSourceInfo {
  Id: string;
  Path: string;
  Protocol: string;
  Container?: string | null;
  Size?: number | null;
  Name?: string | null;
  RunTimeTicks?: number | null;
  SupportsDirectPlay: boolean;
  SupportsDirectStream: boolean;
  SupportsTranscoding: boolean;
  TranscodingUrl?: string | null;
  TranscodingSubProtocol?: string | null;
  TranscodingContainer?: string | null;
  MediaStreams: JellyfinMediaStreamInfo[];
  TranscodingInfo?: JellyfinTranscodingInfo | null;
}

export interface JellyfinPlaybackInfoResponse {
  PlaySessionId: string;
  MediaSources: JellyfinMediaSourceInfo[];
  ErrorCode?: string | null;
}

export interface JellyfinPlaybackSessionRequest {
  ItemId: string;
  MediaSourceId?: string | null;
  PlaySessionId?: string | null;
  PositionTicks?: number | null;
  IsPaused?: boolean | null;
  IsMuted?: boolean | null;
}

export type RequestOptions = GeneratedRequestOptions;

export interface EntityMetadataFlagsPatch {
  isFavorite?: boolean | null;
  isNsfw?: boolean | null;
  isOrganized?: boolean | null;
}

export interface EntityMetadataPatch {
  title?: string | null;
  description?: string | null;
  externalIds: Record<string, string>;
  urls: string[];
  tags: string[];
  studio?: string | null;
  credits: unknown[];
  dates: Record<string, string>;
  stats: Record<string, number>;
  positions: Record<string, number>;
  classification?: string | null;
  rating?: number | null;
  flags?: EntityMetadataFlagsPatch | null;
}

export interface EntityMetadataUpdateRequest {
  fields: string[];
  patch: EntityMetadataPatch;
}

function toNumber(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined) return null;
  const next = typeof value === "number" ? value : Number(value);
  return Number.isFinite(next) ? next : null;
}

function normalizeSettingValue(value: unknown): SettingValue {
  if (Array.isArray(value)) {
    return value.map((item) => String(item).trim()).filter(Boolean);
  }

  if (typeof value === "boolean" || typeof value === "string") return value;
  if (typeof value === "number") return value;
  return "";
}

function normalizeConstraints(
  constraints: GeneratedSettingConstraints | null | undefined,
): SettingConstraints | null {
  if (!constraints) return null;
  return {
    min: toNumber(constraints.min),
    max: toNumber(constraints.max),
    step: toNumber(constraints.step),
    minItems: toNumber(constraints.minItems),
    maxItems: toNumber(constraints.maxItems),
  };
}

function normalizeSettingDescriptor(descriptor: GeneratedSettingDescriptor): SettingDescriptor {
  return {
    ...descriptor,
    value: normalizeSettingValue(descriptor.value),
    defaultValue: normalizeSettingValue(descriptor.defaultValue),
    order: Number(descriptor.order),
    constraints: normalizeConstraints(descriptor.constraints),
  };
}

function normalizeSettingsCatalog(catalog: GeneratedSettingsCatalogResponse): SettingsCatalogResponse {
  return {
    groups: catalog.groups.map((group) => ({
      ...group,
      order: Number(group.order),
      settings: group.settings.map(normalizeSettingDescriptor),
    })),
  };
}

function normalizeSettingsValues(response: GeneratedSettingsValuesResponse): SettingsValuesResponse {
  return {
    values: Object.fromEntries(
      Object.entries(response.values ?? {}).map(([key, value]) => [
        key,
        normalizeSettingValue(value),
      ]),
    ),
  };
}

export function fetchEntities(
  params?: ListEntitiesParams,
  options?: RequestOptions,
): Promise<EntityListResponse> {
  return listEntities(params, requestInit(options)).then((r) =>
    unwrapGenerated(r, "Failed to list entities"),
  );
}

export async function fetchEntityThumbnails(
  ids: string[],
  options?: RequestOptions,
): Promise<EntityThumbnail[]> {
  const uniqueIds = [...new Set(ids.filter(Boolean))];
  if (uniqueIds.length === 0) return [];

  const response = await getEntityThumbnails({ ids: uniqueIds }, undefined, requestInit(options));
  return (response.data as EntityThumbnailBatchResponse).items;
}

export function fetchEntity(id: string, options?: RequestOptions): Promise<EntityCardFull> {
  return getEntity(id, undefined, requestInit(options)).then((r) =>
    unwrapGenerated(r, `Failed to fetch entity ${id}`),
  );
}

export function fetchVideos(
  options?: RequestOptions,
): Promise<VideoListResponse> {
  return listVideos(undefined, requestInit(options)).then((response) => response.data);
}

export function fetchVideo(
  id: string,
  options?: RequestOptions,
): Promise<VideoDetail> {
  return getVideo(id, undefined, requestInit(options)).then((r) =>
    unwrapGenerated(r, `Failed to fetch video ${id}`),
  );
}

export async function fetchJellyfinPlaybackInfo(
  itemId: string,
  request: JellyfinPlaybackInfoRequest = {},
  options?: RequestOptions,
): Promise<JellyfinPlaybackInfoResponse> {
  const response = await fetch(jellyfinApiPath(`/Items/${itemId}/PlaybackInfo`), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
    signal: options?.signal,
  });
  if (!response.ok) {
    throw new Error(await response.text() || `PlaybackInfo ${response.status}`);
  }
  return await response.json() as JellyfinPlaybackInfoResponse;
}

export async function postJellyfinSessionProgress(
  path: "Playing" | "Playing/Progress" | "Playing/Ping" | "Playing/Stopped",
  request: JellyfinPlaybackSessionRequest,
  options?: RequestOptions,
): Promise<void> {
  const payload = request as PlaybackSessionRequest;
  switch (path) {
    case "Playing":
      await postJellyfinSessionPlaying(payload, requestInit(options));
      return;
    case "Playing/Progress":
      await postJellyfinSessionProgressRequest(payload, requestInit(options));
      return;
    case "Playing/Ping":
      await postJellyfinSessionPing(payload, requestInit(options));
      return;
    case "Playing/Stopped":
      await postJellyfinSessionStopped(payload, requestInit(options));
  }
}

export async function markJellyfinUserPlayedItem(
  itemId: string,
  played: boolean,
  options?: RequestOptions,
): Promise<void> {
  if (played) {
    await postJellyfinUserPlayedItem(itemId, requestInit(options));
  } else {
    await deleteJellyfinUserPlayedItem(itemId, requestInit(options));
  }
}

export function fetchSeriesList(
  options?: RequestOptions,
): Promise<VideoSeriesListResponse> {
  return listVideoSeries(undefined, requestInit(options)).then((response) => response.data);
}

export function fetchSeries(
  id: string,
  options?: RequestOptions,
): Promise<VideoSeriesDetail> {
  return getVideoSeries(id, undefined, requestInit(options)).then((r) =>
    unwrapGenerated(r, `Failed to fetch series ${id}`),
  );
}

export function fetchSeason(
  seriesId: string,
  seasonId: string,
  options?: RequestOptions,
): Promise<VideoSeasonDetail> {
  return getVideoSeason(seriesId, seasonId, undefined, requestInit(options)).then((r) =>
    unwrapGenerated(r, `Failed to fetch season ${seasonId}`),
  );
}

export function fetchImages(options?: RequestOptions): Promise<MediaListResponse> {
  return listImages(undefined, requestInit(options)).then((response) => response.data);
}

export function fetchGalleries(options?: RequestOptions): Promise<MediaListResponse> {
  return listGalleries(undefined, requestInit(options)).then((response) => response.data);
}

export function fetchBooks(options?: RequestOptions): Promise<MediaListResponse> {
  return listBooks(undefined, requestInit(options)).then((response) => response.data);
}

export function fetchAudioLibraries(options?: RequestOptions): Promise<MediaListResponse> {
  return listAudioLibraries(undefined, requestInit(options)).then((response) => response.data);
}

export function fetchAudioTracks(options?: RequestOptions): Promise<MediaListResponse> {
  return listAudioTracks(undefined, requestInit(options)).then((response) => response.data);
}

export function fetchImage(id: string, options?: RequestOptions): Promise<ImageDetail> {
  return getImage(id, undefined, requestInit(options)).then((r) =>
    unwrapGenerated(r, `Failed to fetch image ${id}`),
  );
}

export function fetchGallery(id: string, options?: RequestOptions): Promise<GalleryDetail> {
  return getGallery(id, undefined, requestInit(options)).then((r) =>
    unwrapGenerated(r, `Failed to fetch gallery ${id}`),
  );
}

export function fetchBook(id: string, options?: RequestOptions): Promise<BookDetail> {
  return getBook(id, undefined, requestInit(options)).then((r) =>
    unwrapGenerated(r, `Failed to fetch book ${id}`),
  );
}

export function fetchAudioLibrary(id: string, options?: RequestOptions): Promise<AudioLibraryDetail> {
  return getAudioLibrary(id, undefined, requestInit(options)).then((r) =>
    unwrapGenerated(r, `Failed to fetch audio library ${id}`),
  );
}

export function fetchAudioTrack(id: string, options?: RequestOptions): Promise<AudioTrackDetail> {
  return getAudioTrack(id, undefined, requestInit(options)).then((r) =>
    unwrapGenerated(r, `Failed to fetch audio track ${id}`),
  );
}

export function fetchPerson(id: string, options?: RequestOptions): Promise<PersonDetail> {
  return getPerson(id, undefined, requestInit(options)).then((r) =>
    unwrapGenerated(r, `Failed to fetch person ${id}`),
  );
}

export function fetchStudio(id: string, options?: RequestOptions): Promise<StudioDetail> {
  return getStudio(id, undefined, requestInit(options)).then((r) =>
    unwrapGenerated(r, `Failed to fetch studio ${id}`),
  );
}

export function fetchTag(id: string, options?: RequestOptions): Promise<TagDetail> {
  return getTag(id, undefined, requestInit(options)).then((r) =>
    unwrapGenerated(r, `Failed to fetch tag ${id}`),
  );
}

export function fetchPeople(options?: RequestOptions): Promise<TaxonomyListResponse> {
  return listPeople(undefined, requestInit(options)).then((response) => response.data);
}

export function fetchStudios(options?: RequestOptions): Promise<TaxonomyListResponse> {
  return listStudios(undefined, requestInit(options)).then((response) => response.data);
}

export function fetchTags(options?: RequestOptions): Promise<TaxonomyListResponse> {
  return listTags(undefined, requestInit(options)).then((response) => response.data);
}

export function fetchCollection(id: string, options?: RequestOptions): Promise<CollectionDetail> {
  return getCollection(id, undefined, requestInit(options)).then((r) =>
    unwrapGenerated(r, `Failed to fetch collection ${id}`),
  );
}

export function fetchCollections(options?: RequestOptions): Promise<CollectionListResponse> {
  return listCollections(undefined, requestInit(options)).then((response) => response.data);
}

export async function updateEntityPlayback(
  id: string,
  payload: { resumeSeconds?: number | null; durationSeconds?: number | null; completed?: boolean | null },
  options?: RequestOptions,
): Promise<EntityCard> {
  const response = await updateEntityPlaybackRequest(
    id,
    payload as PlaybackUpdateRequest,
    { signal: options?.signal },
  );
  return unwrapGenerated(
    response,
    `Failed to update playback for ${id}`,
  ) as unknown as EntityCard;
}

export async function updateEntityProgress(
  id: string,
  payload: {
    currentEntityId: string;
    unit: string;
    index: number;
    total: number;
    mode?: string | null;
    completed?: boolean | null;
  },
  options?: RequestOptions,
): Promise<EntityCard> {
  return fetchApi<EntityCard>(`/entities/${id}/progress`, {
    method: "PATCH",
    body: JSON.stringify({
      currentEntityId: payload.currentEntityId,
      unit: payload.unit,
      index: payload.index,
      total: payload.total,
      mode: payload.mode ?? null,
      completed: payload.completed ?? null,
    }),
    signal: options?.signal,
  });
}

export interface EntityMarkerWriteRequest {
  title: string;
  seconds: number;
  endSeconds?: number | null;
}

async function writeEntityMarker(
  id: string,
  method: "POST" | "PATCH" | "DELETE",
  markerId: string | null,
  payload?: EntityMarkerWriteRequest,
  options?: RequestOptions,
): Promise<EntityCard> {
  const requestOptions = { signal: options?.signal };
  const fallback = `Failed to ${method.toLowerCase()} marker for ${id}`;
  const markerPayload = payload
    ? {
        ...payload,
        endSeconds: payload.endSeconds ?? null,
      }
    : undefined;
  if (method === "POST" && payload) {
    return unwrapGenerated(
      await createEntityMarkerRequest(id, markerPayload!, requestOptions),
      fallback,
    ) as unknown as EntityCard;
  }
  if (method === "PATCH" && markerId && payload) {
    return unwrapGenerated(
      await updateEntityMarkerRequest(id, markerId, markerPayload!, requestOptions),
      fallback,
    ) as unknown as EntityCard;
  }
  if (method === "DELETE" && markerId) {
    return unwrapGenerated(
      await deleteEntityMarkerRequest(id, markerId, requestOptions),
      fallback,
    ) as unknown as EntityCard;
  }

  throw new Error(fallback);
}

export function createEntityMarker(
  id: string,
  payload: EntityMarkerWriteRequest,
  options?: RequestOptions,
): Promise<EntityCard> {
  return writeEntityMarker(id, "POST", null, payload, options);
}

export function updateEntityMarker(
  id: string,
  markerId: string,
  payload: EntityMarkerWriteRequest,
  options?: RequestOptions,
): Promise<EntityCard> {
  return writeEntityMarker(id, "PATCH", markerId, payload, options);
}

export function deleteEntityMarker(
  id: string,
  markerId: string,
  options?: RequestOptions,
): Promise<EntityCard> {
  return writeEntityMarker(id, "DELETE", markerId, undefined, options);
}

export function fetchJobs(options?: RequestOptions): Promise<JobListResponse> {
  return listJobs(undefined, requestInit(options)).then((response) => response.data);
}

export function fetchWorkerHealth(options?: RequestOptions): Promise<WorkerHealthResponse> {
  return fetchApi<WorkerHealthResponse>("/health/worker", { signal: options?.signal });
}

export async function createJob(
  type: string,
  options?: RequestOptions,
): Promise<JobCreateResponse> {
  const response = await createJobRequest(type, requestInit(options));
  return unwrapGenerated(
    response,
    `Failed to queue ${type}`,
    [200, 202],
  );
}

export async function cancelJobs(
  type?: string | null,
  options?: RequestOptions,
): Promise<JobCancelResponse> {
  const response = await cancelJobsRequest(type ? { type } : undefined, requestInit(options));
  return unwrapGenerated(
    response,
    "Failed to cancel jobs",
  );
}

export async function cancelJobRun(
  id: string,
  options?: RequestOptions,
): Promise<JobCancelResponse> {
  const response = await cancelJobRunRequest(id, requestInit(options));
  return unwrapGenerated(
    response,
    "Failed to cancel job",
  );
}

export async function clearJobFailures(
  type?: string | null,
  options?: RequestOptions,
): Promise<JobFailureClearResponse> {
  const response = await clearJobFailuresRequest(type ? { type } : undefined, requestInit(options));
  return unwrapGenerated(
    response,
    "Failed to clear job failures",
  );
}

export async function fetchSettings(options?: RequestOptions): Promise<SettingsResponse> {
  const response = unwrapGenerated<GeneratedSettingsCatalogResponse>(await getSettings(requestInit(options)), "Failed to load settings");
  return normalizeSettingsCatalog(response);
}

export async function fetchSetting(
  key: string,
  options?: RequestOptions,
): Promise<SettingDescriptor> {
  const response = unwrapGenerated<GeneratedSettingDescriptor>(
    await getSettingRequest(key, requestInit(options)),
    "Failed to load setting",
  );
  return normalizeSettingDescriptor(response);
}

export async function fetchSettingsValues(
  keys: string[] = [],
  options?: RequestOptions,
): Promise<SettingsValuesResponse> {
  const search = new URLSearchParams();
  for (const key of keys) {
    search.append("keys", key);
  }

  const query = search.toString();
  const response = await fetchApi<GeneratedSettingsValuesResponse>(
    query ? `/settings/values?${query}` : "/settings/values",
    { signal: options?.signal },
  );
  return normalizeSettingsValues(response);
}

export async function updateSetting(
  key: string,
  value: SettingValue,
  options?: RequestOptions,
): Promise<SettingDescriptor> {
  const response = unwrapGenerated<GeneratedSettingDescriptor>(
    await updateSettingRequest(
      key,
      { value } as unknown as Parameters<typeof updateSettingRequest>[1],
      requestInit(options),
    ),
    "Failed to save setting",
  );
  return normalizeSettingDescriptor(response);
}

export async function updateSettings(
  values: Record<string, SettingValue>,
  options?: RequestOptions,
): Promise<SettingsCatalogResponse> {
  const response = unwrapGenerated<GeneratedSettingsCatalogResponse>(
    await updateSettingsRequest(
      { values } as unknown as Parameters<typeof updateSettingsRequest>[0],
      requestInit(options),
    ),
    "Failed to save settings",
  );
  return normalizeSettingsCatalog(response);
}

export async function resetSetting(
  key: string,
  options?: RequestOptions,
): Promise<SettingDescriptor> {
  const response = unwrapGenerated<GeneratedSettingDescriptor>(
    await resetSettingRequest(key, requestInit(options)),
    "Failed to reset setting",
  );
  return normalizeSettingDescriptor(response);
}

export async function fetchLibraryConfig(options?: RequestOptions): Promise<LibraryConfigResponse> {
  const response = unwrapGenerated(
    await getLibraryConfig(requestInit(options)),
    "Failed to load settings",
  ) as GeneratedLibraryConfigResponse;
  return {
    settings: normalizeSettingsCatalog(response.settings),
    roots: response.roots,
  };
}

export async function browseLibraryPath(
  targetPath?: string,
  options?: RequestOptions,
): Promise<LibraryBrowse> {
  return unwrapGenerated(
    await browseLibraryPathRequest(targetPath ? { path: targetPath } : undefined, requestInit(options)),
    "Failed to browse folders",
  );
}

export async function createLibraryRoot(
  payload: Partial<LibraryRoot> & { path: string },
  options?: RequestOptions,
): Promise<LibraryRoot> {
  return unwrapGenerated(
    await createLibraryRootRequest(payload as unknown as Parameters<typeof createLibraryRootRequest>[0], requestInit(options)),
    "Failed to add library root",
  );
}

export async function updateLibraryRoot(
  id: string,
  payload: Partial<LibraryRoot>,
  options?: RequestOptions,
): Promise<LibraryRoot> {
  const response = await updateLibraryRootRequest(
    id,
    payload as unknown as Parameters<typeof updateLibraryRootRequest>[1],
    requestInit(options),
  );
  return unwrapGenerated(
    response,
    "Failed to update library root",
  );
}

export async function deleteLibraryRoot(
  id: string,
  options?: RequestOptions,
): Promise<{ ok: true }> {
  const response = await deleteLibraryRootRequest(id, requestInit(options));
  return unwrapGenerated(
    response,
    "Failed to remove library root",
  );
}

export async function fetchFileRoots(
  options?: RequestOptions,
): Promise<FileRootsResponse> {
  return unwrapGenerated(
    await listFileRoots(undefined, requestInit(options)),
    "Failed to load file roots",
  );
}

export async function fetchFileChildren(
  rootId: string,
  path = "",
  options?: RequestOptions,
): Promise<FileChildrenResponse> {
  return unwrapGenerated(
    await listFileChildren({ rootId, ...(path ? { path } : {}) }, requestInit(options)),
    "Failed to load folder",
  );
}

export async function fetchFileDetail(
  rootId: string,
  path = "",
  options?: RequestOptions,
): Promise<FileDetail> {
  return unwrapGenerated(
    await getFileDetail({ rootId, ...(path ? { path } : {}) }, requestInit(options)),
    "Failed to load file details",
  ) as unknown as FileDetail;
}

export function fileContentUrl(rootId: string, path = ""): string {
  return apiPath(getGetFileContentUrl({ rootId, path }));
}

export function entityFileUrl(entityId: string, role: EntityFileRoleCode): string {
  return apiPath(`/entities/${encodeURIComponent(entityId)}/files/${encodeURIComponent(role)}`);
}

export async function createFileFolder(
  payload: FileCreateFolderRequest,
  options?: RequestOptions,
): Promise<FileOperationResponse> {
  return unwrapGenerated(
    await createFileFolderRequest(payload, undefined, requestInit(options)),
    "Failed to create folder",
  );
}

export async function uploadFiles(
  rootId: string,
  targetPath: string,
  items: FileUploadItem[],
  options?: RequestOptions,
): Promise<FileOperationResponse> {
  const form = new FormData();
  form.append("rootId", rootId);
  form.append("targetPath", targetPath);
  for (const item of items) {
    form.append("relativePaths", item.relativePath);
    form.append("files", item.file);
  }

  return unwrapGenerated(
    await uploadFilesRequest({ body: form, signal: options?.signal }),
    "Failed to upload files",
  );
}

export async function renameFile(
  payload: FileRenameRequest,
  options?: RequestOptions,
): Promise<FileOperationResponse> {
  return unwrapGenerated(
    await renameFileRequest(payload, undefined, requestInit(options)),
    "Failed to rename file",
  );
}

export async function moveFile(
  payload: FileMoveRequest,
  options?: RequestOptions,
): Promise<FileOperationResponse> {
  return unwrapGenerated(
    await moveFileRequest(payload, undefined, requestInit(options)),
    "Failed to move file",
  );
}

export async function deleteFile(
  rootId: string,
  path: string,
  options?: RequestOptions,
): Promise<FileOperationResponse> {
  return unwrapGenerated(
    await deleteFileRequest({ rootId, path }, { signal: options?.signal }),
    "Failed to delete file",
  );
}

export async function excludeFile(
  payload: FileExclusionRequest,
  options?: RequestOptions,
): Promise<FileOperationResponse> {
  return unwrapGenerated(
    await excludeFileRequest(payload, undefined, requestInit(options)),
    "Failed to exclude file",
  );
}

export async function removeFileExclusion(
  payload: FileExclusionRequest,
  options?: RequestOptions,
): Promise<FileOperationResponse> {
  return unwrapGenerated(
    await removeFileExclusionRequest(payload, requestInit(options)),
    "Failed to remove file exclusion",
  );
}

export async function rescanFileRoot(
  payload: FileRescanRequest,
  options?: RequestOptions,
): Promise<FileOperationResponse> {
  return unwrapGenerated(
    await rescanFileRootRequest(payload, undefined, requestInit(options)),
    "Failed to queue file rescan",
  );
}

export interface EntityRefreshResponse {
  jobId: string | null;
  alreadyPending: boolean;
}

export function refreshEntity(
  entityId: string,
  options?: RequestOptions,
): Promise<EntityRefreshResponse> {
  return fetchApi<EntityRefreshResponse>(`/entities/${entityId}/refresh`, {
    method: "POST",
    signal: options?.signal,
  });
}

export async function rebuildPreviews(
  options?: RequestOptions,
): Promise<BulkJobResponse> {
  const response = await rebuildPreviewsRequest({ signal: options?.signal });
  return unwrapGenerated(
    response,
    "Failed to queue preview rebuild",
  );
}

export async function backfillFingerprints(
  options?: RequestOptions,
): Promise<BulkJobResponse> {
  const response = await backfillFingerprintsRequest({ signal: options?.signal });
  return unwrapGenerated(
    response,
    "Failed to queue fingerprint backfill",
  );
}
