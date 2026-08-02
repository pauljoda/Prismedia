import {
  createVideoPlaybackPlan as createVideoPlaybackPlanRequest,
  pingVideoPlaybackSession,
  progressVideoPlaybackSession,
  startVideoPlaybackSession,
  stopVideoPlaybackSession,
} from "$lib/api/generated/prismedia";
import type {
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

function localUtcOffsetMinutes(): number {
  return typeof Date === "undefined" ? 0 : -new Date().getTimezoneOffset();
}
