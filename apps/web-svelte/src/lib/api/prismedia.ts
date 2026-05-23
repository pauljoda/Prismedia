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
  rescanFileRoot as rescanFileRootRequest,
  updateEntityFlags as updateEntityFlagsRequest,
  updateEntityMarker as updateEntityMarkerRequest,
  updateEntityPlayback as updateEntityPlaybackRequest,
  updateEntityRating as updateEntityRatingRequest,
  getSettings,
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
  updateLibrarySettings as updateLibrarySettingsRequest,
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
  LibraryRoot as GeneratedLibraryRoot,
  LibrarySettings as GeneratedLibrarySettings,
  PersonDetail as GeneratedPersonDetail,
  PlaybackSessionRequest as GeneratedPlaybackSessionRequest,
  PlaybackUpdateRequest as GeneratedPlaybackUpdateRequest,
  SettingsResponse as GeneratedSettingsResponse,
  StudioDetail as GeneratedStudioDetail,
  TagDetail as GeneratedTagDetail,
  VideoDetail as GeneratedVideoDetail,
  VideoSeriesDetail as GeneratedVideoSeriesDetail,
  VideoSeasonDetail as GeneratedVideoSeasonDetail,
} from "./generated/model";
import { fetchApi, jellyfinApiPath, apiPath } from "./orval-fetch";
import type { EntityFileRoleCode } from "$lib/entities/entity-codes";

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
export type SettingsResponse = GeneratedSettingsResponse;
export type MediaListResponse = GeneratedEntityListResponse;
export interface EntityReference {
  id: string;
  kind: string;
  title: string;
  thumbnailUrl?: string | null;
}
type NumericLibrarySettingsFields =
  | "scanIntervalMinutes"
  | "trickplayIntervalSeconds"
  | "previewClipDurationSeconds"
  | "thumbnailQuality"
  | "trickplayQuality"
  | "backgroundWorkerConcurrency"
  | "subtitleFontScale"
  | "subtitlePositionPercent"
  | "subtitleOpacity";

export type LibrarySettings = Omit<GeneratedLibrarySettings, NumericLibrarySettingsFields> & {
  [K in NumericLibrarySettingsFields]: number;
};
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
type FileMoveRequest = GeneratedFileMoveRequest;
type FileRenameRequest = GeneratedFileRenameRequest;
type FileRescanRequest = GeneratedFileRescanRequest;
type FileRootsResponse = GeneratedFileRootsResponse;
type PlaybackSessionRequest = GeneratedPlaybackSessionRequest;
type PlaybackUpdateRequest = GeneratedPlaybackUpdateRequest;
export interface LibraryConfigResponse {
  settings: LibrarySettings;
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

export interface RequestOptions {
  signal?: AbortSignal;
}

export interface EntityMetadataUpdateOptions extends RequestOptions {
  kind?: string | null;
}

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

type GeneratedResponse<T> = {
  data: T;
  status: number;
};

function problemMessage(data: unknown): string | null {
  if (data && typeof data === "object") {
    const record = data as Record<string, unknown>;
    if (typeof record.message === "string") return record.message;
    if (typeof record.error === "string") return record.error;
    if (typeof record.detail === "string") return record.detail;
    if (typeof record.title === "string") return record.title;
  }

  if (typeof data === "string" && data.trim()) return data;
  return null;
}

function unwrapGenerated<T>(
  response: GeneratedResponse<T>,
  fallback: string,
  okStatuses: readonly number[] = [200],
): T {
  if (!okStatuses.includes(response.status)) {
    throw new Error(problemMessage(response.data) ?? fallback);
  }

  return response.data;
}

export function fetchEntities(
  params?: { kind?: string; query?: string; cursor?: string; hideNsfw?: boolean; limit?: number },
  options?: RequestOptions,
): Promise<EntityListResponse> {
  // Cast: hideNsfw is accepted by the backend but not yet in the generated OpenAPI type.
  return listEntities(params as Record<string, string | boolean | undefined>, { signal: options?.signal }).then((response) => {
    if (response.status !== 200) {
      throw new Error(response.data.message);
    }

    return response.data;
  });
}

export async function fetchEntityThumbnails(
  ids: string[],
  options?: RequestOptions,
): Promise<EntityThumbnail[]> {
  const uniqueIds = [...new Set(ids.filter(Boolean))];
  if (uniqueIds.length === 0) return [];

  const response = await getEntityThumbnails({ ids: uniqueIds }, { signal: options?.signal });
  return (response.data as EntityThumbnailBatchResponse).items;
}

export function fetchEntity(id: string, options?: RequestOptions): Promise<EntityCardFull> {
  return getEntity(id, { signal: options?.signal }).then((response) => {
    if (response.status !== 200) {
      throw new Error(response.data.message);
    }

    return response.data;
  });
}

export function fetchVideos(
  options?: RequestOptions,
): Promise<VideoListResponse> {
  return listVideos(undefined, { signal: options?.signal }).then((response) => response.data);
}

export function fetchVideo(
  id: string,
  options?: RequestOptions,
): Promise<VideoDetail> {
  return getVideo(id, { signal: options?.signal }).then((response) => {
    if (response.status !== 200) {
      throw new Error(response.data.message);
    }

    return response.data;
  });
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
      await postJellyfinSessionPlaying(payload, { signal: options?.signal });
      return;
    case "Playing/Progress":
      await postJellyfinSessionProgressRequest(payload, { signal: options?.signal });
      return;
    case "Playing/Ping":
      await postJellyfinSessionPing(payload, { signal: options?.signal });
      return;
    case "Playing/Stopped":
      await postJellyfinSessionStopped(payload, { signal: options?.signal });
  }
}

export async function markJellyfinUserPlayedItem(
  itemId: string,
  played: boolean,
  options?: RequestOptions,
): Promise<void> {
  if (played) {
    await postJellyfinUserPlayedItem(itemId, { signal: options?.signal });
  } else {
    await deleteJellyfinUserPlayedItem(itemId, { signal: options?.signal });
  }
}

export function fetchSeriesList(
  options?: RequestOptions,
): Promise<VideoSeriesListResponse> {
  return listVideoSeries(undefined, { signal: options?.signal }).then((response) => response.data);
}

export function fetchSeries(
  id: string,
  options?: RequestOptions,
): Promise<VideoSeriesDetail> {
  return getVideoSeries(id, { signal: options?.signal }).then((response) => {
    if (response.status !== 200) {
      throw new Error(response.data.message);
    }

    return response.data;
  });
}

export function fetchSeason(
  seriesId: string,
  seasonId: string,
  options?: RequestOptions,
): Promise<VideoSeasonDetail> {
  return getVideoSeason(seriesId, seasonId, { signal: options?.signal }).then((response) => {
    if (response.status !== 200) {
      throw new Error(response.data.message);
    }

    return response.data;
  });
}

export function fetchImages(options?: RequestOptions): Promise<MediaListResponse> {
  return listImages(undefined, { signal: options?.signal }).then((response) => response.data);
}

export function fetchGalleries(options?: RequestOptions): Promise<MediaListResponse> {
  return listGalleries(undefined, { signal: options?.signal }).then((response) => response.data);
}

export function fetchBooks(options?: RequestOptions): Promise<MediaListResponse> {
  return listBooks(undefined, { signal: options?.signal }).then((response) => response.data);
}

export function fetchAudioLibraries(options?: RequestOptions): Promise<MediaListResponse> {
  return listAudioLibraries(undefined, { signal: options?.signal }).then((response) => response.data);
}

export function fetchAudioTracks(options?: RequestOptions): Promise<MediaListResponse> {
  return listAudioTracks(undefined, { signal: options?.signal }).then((response) => response.data);
}

export function fetchImage(id: string, options?: RequestOptions): Promise<ImageDetail> {
  return getImage(id, { signal: options?.signal }).then((response) => {
    if (response.status !== 200) throw new Error(response.data.message);
    return response.data;
  });
}

export function fetchGallery(id: string, options?: RequestOptions): Promise<GalleryDetail> {
  return getGallery(id, { signal: options?.signal }).then((response) => {
    if (response.status !== 200) throw new Error(response.data.message);
    return response.data;
  });
}

export function fetchBook(id: string, options?: RequestOptions): Promise<BookDetail> {
  return getBook(id, { signal: options?.signal }).then((response) => {
    if (response.status !== 200) throw new Error(response.data.message);
    return response.data;
  });
}

export function fetchAudioLibrary(id: string, options?: RequestOptions): Promise<AudioLibraryDetail> {
  return getAudioLibrary(id, { signal: options?.signal }).then((response) => {
    if (response.status !== 200) throw new Error(response.data.message);
    return response.data;
  });
}

export function fetchAudioTrack(id: string, options?: RequestOptions): Promise<AudioTrackDetail> {
  return getAudioTrack(id, { signal: options?.signal }).then((response) => {
    if (response.status !== 200) throw new Error(response.data.message);
    return response.data;
  });
}

export function fetchPerson(id: string, options?: RequestOptions): Promise<PersonDetail> {
  return getPerson(id, { signal: options?.signal }).then((response) => {
    if (response.status !== 200) throw new Error(response.data.message);
    return response.data;
  });
}

export function fetchStudio(id: string, options?: RequestOptions): Promise<StudioDetail> {
  return getStudio(id, { signal: options?.signal }).then((response) => {
    if (response.status !== 200) throw new Error(response.data.message);
    return response.data;
  });
}

export function fetchTag(id: string, options?: RequestOptions): Promise<TagDetail> {
  return getTag(id, { signal: options?.signal }).then((response) => {
    if (response.status !== 200) throw new Error(response.data.message);
    return response.data;
  });
}

export function fetchPeople(options?: RequestOptions): Promise<TaxonomyListResponse> {
  return listPeople(undefined, { signal: options?.signal }).then((response) => response.data);
}

export function fetchStudios(options?: RequestOptions): Promise<TaxonomyListResponse> {
  return listStudios(undefined, { signal: options?.signal }).then((response) => response.data);
}

export function fetchTags(options?: RequestOptions): Promise<TaxonomyListResponse> {
  return listTags(undefined, { signal: options?.signal }).then((response) => response.data);
}

export function fetchCollection(id: string, options?: RequestOptions): Promise<CollectionDetail> {
  return getCollection(id, { signal: options?.signal }).then((response) => {
    if (response.status !== 200) throw new Error(response.data.message);
    return response.data;
  });
}

export function fetchCollections(options?: RequestOptions): Promise<CollectionListResponse> {
  return listCollections(undefined, { signal: options?.signal }).then((response) => response.data);
}

export function updateEntityRating(
  id: string,
  value: number | null,
): Promise<unknown> {
  return updateEntityRatingRequest(id, { value });
}

export function updateEntityFlags(
  id: string,
  flags: { isFavorite?: boolean | null; isNsfw?: boolean | null; isOrganized?: boolean | null },
): Promise<unknown> {
  return updateEntityFlagsRequest(id, {
    isFavorite: flags.isFavorite ?? null,
    isNsfw: flags.isNsfw ?? null,
    isOrganized: flags.isOrganized ?? null,
  });
}

export function updateEntityMetadata(
  id: string,
  request: EntityMetadataUpdateRequest,
  options?: EntityMetadataUpdateOptions,
): Promise<EntityDetailCard> {
  const kindPath = options?.kind ? `/${encodeURIComponent(options.kind)}` : "";
  return fetchApi<EntityDetailCard>(`/entities${kindPath}/${id}`, {
    method: "PATCH",
    body: JSON.stringify(request),
    signal: options?.signal,
  });
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
    response as unknown as GeneratedResponse<EntityCard>,
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
      await createEntityMarkerRequest(id, markerPayload!, requestOptions) as unknown as GeneratedResponse<EntityCard>,
      fallback,
    ) as unknown as EntityCard;
  }
  if (method === "PATCH" && markerId && payload) {
    return unwrapGenerated(
      await updateEntityMarkerRequest(id, markerId, markerPayload!, requestOptions) as unknown as GeneratedResponse<EntityCard>,
      fallback,
    ) as unknown as EntityCard;
  }
  if (method === "DELETE" && markerId) {
    return unwrapGenerated(
      await deleteEntityMarkerRequest(id, markerId, requestOptions) as unknown as GeneratedResponse<EntityCard>,
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
  return listJobs({ signal: options?.signal }).then((response) => response.data);
}

export async function createJob(
  type: string,
  options?: RequestOptions,
): Promise<JobCreateResponse> {
  const response = await createJobRequest(type, { signal: options?.signal });
  return unwrapGenerated(
    response as unknown as GeneratedResponse<JobCreateResponse>,
    `Failed to queue ${type}`,
    [200, 202],
  );
}

export async function cancelJobs(
  type?: string | null,
  options?: RequestOptions,
): Promise<JobCancelResponse> {
  const response = await cancelJobsRequest(type ? { type } : undefined, { signal: options?.signal });
  return unwrapGenerated(
    response as unknown as GeneratedResponse<JobCancelResponse>,
    "Failed to cancel jobs",
  );
}

export async function cancelJobRun(
  id: string,
  options?: RequestOptions,
): Promise<JobCancelResponse> {
  const response = await cancelJobRunRequest(id, { signal: options?.signal });
  return unwrapGenerated(
    response as unknown as GeneratedResponse<JobCancelResponse>,
    "Failed to cancel job",
  );
}

export async function clearJobFailures(
  type?: string | null,
  options?: RequestOptions,
): Promise<JobFailureClearResponse> {
  const response = await clearJobFailuresRequest(type ? { type } : undefined, { signal: options?.signal });
  return unwrapGenerated(
    response as unknown as GeneratedResponse<JobFailureClearResponse>,
    "Failed to clear job failures",
  );
}

export function fetchSettings(options?: RequestOptions): Promise<SettingsResponse> {
  return getSettings({ signal: options?.signal }).then((response) => response.data);
}

export async function fetchLibraryConfig(
  options?: RequestOptions,
): Promise<LibraryConfigResponse> {
  return unwrapGenerated(
    await getLibraryConfig({ signal: options?.signal }),
    "Failed to load settings",
  ) as unknown as LibraryConfigResponse;
}

export async function updateLibrarySettings(
  payload: Partial<LibrarySettings>,
  options?: RequestOptions,
): Promise<LibrarySettings> {
  return unwrapGenerated(
    await updateLibrarySettingsRequest(
      payload as unknown as Parameters<typeof updateLibrarySettingsRequest>[0],
      { signal: options?.signal },
    ),
    "Failed to save settings",
  ) as unknown as LibrarySettings;
}

export async function browseLibraryPath(
  targetPath?: string,
  options?: RequestOptions,
): Promise<LibraryBrowse> {
  return unwrapGenerated(
    await browseLibraryPathRequest(targetPath ? { path: targetPath } : undefined, { signal: options?.signal }),
    "Failed to browse folders",
  );
}

export async function createLibraryRoot(
  payload: Partial<LibraryRoot> & { path: string },
  options?: RequestOptions,
): Promise<LibraryRoot> {
  return unwrapGenerated(
    await createLibraryRootRequest(payload as unknown as Parameters<typeof createLibraryRootRequest>[0], { signal: options?.signal }),
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
    { signal: options?.signal },
  );
  return unwrapGenerated(
    response as unknown as GeneratedResponse<LibraryRoot>,
    "Failed to update library root",
  );
}

export async function deleteLibraryRoot(
  id: string,
  options?: RequestOptions,
): Promise<{ ok: true }> {
  const response = await deleteLibraryRootRequest(id, { signal: options?.signal });
  return unwrapGenerated(
    response as unknown as GeneratedResponse<{ ok: true }>,
    "Failed to remove library root",
  );
}

export async function fetchFileRoots(
  options?: RequestOptions,
): Promise<FileRootsResponse> {
  return unwrapGenerated(
    await listFileRoots({ signal: options?.signal }),
    "Failed to load file roots",
  );
}

export async function fetchFileChildren(
  rootId: string,
  path = "",
  options?: RequestOptions,
): Promise<FileChildrenResponse> {
  return unwrapGenerated(
    await listFileChildren({ rootId, ...(path ? { path } : {}) }, { signal: options?.signal }),
    "Failed to load folder",
  );
}

export async function fetchFileDetail(
  rootId: string,
  path = "",
  options?: RequestOptions,
): Promise<FileDetail> {
  return unwrapGenerated(
    await getFileDetail({ rootId, ...(path ? { path } : {}) }, { signal: options?.signal }),
    "Failed to load file details",
  );
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
    await createFileFolderRequest(payload, { signal: options?.signal }),
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
    await renameFileRequest(payload, { signal: options?.signal }),
    "Failed to rename file",
  );
}

export async function moveFile(
  payload: FileMoveRequest,
  options?: RequestOptions,
): Promise<FileOperationResponse> {
  return unwrapGenerated(
    await moveFileRequest(payload, { signal: options?.signal }),
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

export async function rescanFileRoot(
  payload: FileRescanRequest,
  options?: RequestOptions,
): Promise<FileOperationResponse> {
  return unwrapGenerated(
    await rescanFileRootRequest(payload, { signal: options?.signal }),
    "Failed to queue file rescan",
  );
}

export interface EntityRefreshResponse {
  jobId: string | null;
  alreadyPending: boolean;
}

export async function refreshEntity(
  entityId: string,
  options?: RequestOptions,
): Promise<EntityRefreshResponse> {
  const response = await fetch(`/api/entities/${entityId}/refresh`, {
    method: "POST",
    signal: options?.signal,
  });
  if (!response.ok) throw new Error(`Failed to queue entity refresh: ${response.status}`);
  return response.json();
}

export async function rebuildPreviews(
  options?: RequestOptions,
): Promise<BulkJobResponse> {
  const response = await rebuildPreviewsRequest({ signal: options?.signal });
  return unwrapGenerated(
    response as unknown as GeneratedResponse<BulkJobResponse>,
    "Failed to queue preview rebuild",
  );
}

export async function backfillFingerprints(
  options?: RequestOptions,
): Promise<BulkJobResponse> {
  const response = await backfillFingerprintsRequest({ signal: options?.signal });
  return unwrapGenerated(
    response as unknown as GeneratedResponse<BulkJobResponse>,
    "Failed to queue fingerprint backfill",
  );
}
