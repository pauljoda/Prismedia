import {
  deleteJellyfinUserPlayedItem,
  postJellyfinSessionPing,
  postJellyfinSessionPlaying,
  postJellyfinSessionProgress as postJellyfinSessionProgressRequest,
  postJellyfinSessionStopped,
  postJellyfinUserPlayedItem,
  recordAudioTrackPlay as recordAudioTrackPlayRequest,
  updateEntityPlayback as updateEntityPlaybackRequest,
  updateEntityProgress as updateEntityProgressRequest,
} from "$lib/api/generated/prismedia";
import type {
  EntityCard,
  EntityProgressUpdateRequest,
  PlaybackSessionRequest,
  PlaybackUpdateRequest,
} from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";
import { jellyfinApiPath } from "$lib/api/orval-fetch";

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
  IsDefault?: boolean;
  IsForced?: boolean;
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

export async function updateEntityPlayback(
  id: string,
  payload: { resumeSeconds?: number | null; durationSeconds?: number | null; completed?: boolean | null },
  options?: RequestOptions,
): Promise<EntityCard> {
  return unwrapGenerated(
    await updateEntityPlaybackRequest(id, payload as PlaybackUpdateRequest, requestInit(options)),
    `Failed to update playback for ${id}`,
  );
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
  return unwrapGenerated(
    await updateEntityProgressRequest(
      id,
      {
        currentEntityId: payload.currentEntityId,
        unit: payload.unit,
        index: payload.index,
        total: payload.total,
        mode: payload.mode ?? null,
        completed: payload.completed ?? null,
      } as EntityProgressUpdateRequest,
      requestInit(options),
    ),
    `Failed to update progress for ${id}`,
  );
}

export async function recordAudioTrackPlay(
  id: string,
  options?: RequestOptions,
): Promise<EntityCard> {
  return unwrapGenerated(
    await recordAudioTrackPlayRequest(id, requestInit(options)),
    `Failed to record audio track play for ${id}`,
  );
}
