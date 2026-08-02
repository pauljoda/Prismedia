import type {
  ConsumptionActivityKindCode,
  ConsumptionEventKindCode,
  ProgressUnitCode,
  ReaderModeCode,
} from "$lib/api/generated/codes";
import {
  createVideoPlaybackPlan as createVideoPlaybackPlanRequest,
  createEntityPlaybackEvent as createEntityPlaybackEventRequest,
  pingVideoPlaybackSession,
  progressVideoPlaybackSession,
  recordAudioTrackPlay as recordAudioTrackPlayRequest,
  startVideoPlaybackSession,
  stopVideoPlaybackSession,
  updateEntityPlayback as updateEntityPlaybackRequest,
  updateEntityProgress as updateEntityProgressRequest,
} from "$lib/api/generated/prismedia";
import type {
  EntityCard,
  PlaybackEventCreateRequest,
  EntityProgressUpdateRequest,
  PlaybackUpdateRequest,
  VideoPlaybackPlanRequest,
  VideoPlaybackPlanResponse,
  VideoPlaybackSessionRequest,
} from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";

export type PlaybackSessionEvent = "start" | "progress" | "ping" | "stop";
export interface VideoPlaybackSessionPayload {
  entityId: string;
  sessionId?: string | null;
  positionSeconds?: number | null;
  durationSeconds?: number | null;
  completed?: boolean | null;
  activitySeconds?: number | null;
  utcOffsetMinutes?: number | null;
}

type PlaybackSessionRequest = (
  request: VideoPlaybackSessionRequest,
  options?: RequestInit,
) => Promise<{ data: unknown; status: number }>;

const playbackSessionRequests: Record<PlaybackSessionEvent, PlaybackSessionRequest> = {
  start: startVideoPlaybackSession,
  progress: progressVideoPlaybackSession,
  ping: pingVideoPlaybackSession,
  stop: stopVideoPlaybackSession,
};

export async function createVideoPlaybackPlan(
  entityId: string,
  request: VideoPlaybackPlanRequest,
  options?: RequestOptions,
): Promise<VideoPlaybackPlanResponse> {
  return unwrapGenerated(
    await createVideoPlaybackPlanRequest(entityId, request, requestInit(options)),
    `Failed to create a playback plan for ${entityId}`,
  );
}

export async function reportVideoPlayback(
  event: PlaybackSessionEvent,
  request: VideoPlaybackSessionPayload,
  options?: RequestOptions,
): Promise<void> {
  const payload: VideoPlaybackSessionRequest = {
    entityId: request.entityId,
    sessionId: request.sessionId ?? null,
    positionSeconds: request.positionSeconds ?? null,
    durationSeconds: request.durationSeconds ?? null,
    completed: request.completed ?? null,
    activitySeconds: request.activitySeconds ?? null,
    utcOffsetMinutes: request.utcOffsetMinutes ?? localUtcOffsetMinutes(),
  };
  return unwrapGenerated(
    await playbackSessionRequests[event](payload, requestInit(options)),
    `Failed to report playback ${event}`,
    [204],
  );
}

export async function updateEntityPlayback(
  id: string,
  payload: {
    resumeSeconds?: number | null;
    durationSeconds?: number | null;
    completed?: boolean | null;
    utcOffsetMinutes?: number | null;
  },
  options?: RequestOptions,
): Promise<EntityCard> {
  return unwrapGenerated(
    await updateEntityPlaybackRequest(
      id,
      { ...payload, utcOffsetMinutes: payload.utcOffsetMinutes ?? localUtcOffsetMinutes() } as PlaybackUpdateRequest,
      requestInit(options),
    ),
    `Failed to update playback for ${id}`,
  );
}

export async function updateEntityProgress(
  id: string,
  payload: {
    currentEntityId: string;
    unit: ProgressUnitCode;
    index: number;
    total: number;
    mode?: ReaderModeCode | null;
    completed?: boolean | null;
    reset?: boolean;
    location?: string | null;
    activitySeconds?: number | null;
    activityKind?: ConsumptionActivityKindCode;
    utcOffsetMinutes?: number | null;
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
        reset: payload.reset ?? false,
        location: payload.location ?? null,
        activitySeconds: payload.activitySeconds ?? null,
        activityKind: payload.activityKind,
        utcOffsetMinutes: payload.utcOffsetMinutes ?? localUtcOffsetMinutes(),
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

export async function recordEntityPlaybackEvent(
  id: string,
  payload: {
    kind: ConsumptionEventKindCode;
    occurredAt?: string | null;
    positionSeconds?: number | null;
    durationSeconds?: number | null;
    sessionId?: string | null;
  },
  options?: RequestOptions,
): Promise<EntityCard> {
  return unwrapGenerated(
    await createEntityPlaybackEventRequest(
      id,
      {
        kind: payload.kind,
        occurredAt: payload.occurredAt ?? null,
        positionSeconds: payload.positionSeconds ?? null,
        durationSeconds: payload.durationSeconds ?? null,
        sessionId: payload.sessionId ?? null,
      } as PlaybackEventCreateRequest,
      requestInit(options),
    ),
    `Failed to record playback event for ${id}`,
  );
}

function localUtcOffsetMinutes(): number {
  return typeof Date === "undefined" ? 0 : -new Date().getTimezoneOffset();
}
