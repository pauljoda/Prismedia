import { fetchApi } from "$lib/api/orval-fetch";
import type { RequestOptions } from "$lib/api/generated-response";
import type { PlaybackStatisticsResponse } from "$lib/api/generated/model";
import type { EntityKindCode, PlaybackEventKindCode } from "$lib/entities/entity-codes";

export interface PlaybackStatisticsParams {
  from?: string;
  to?: string;
  kind?: EntityKindCode;
  eventKind?: PlaybackEventKindCode;
  hideNsfw?: boolean;
  userId?: string;
  allUsers?: boolean;
}

export function fetchPlaybackStatistics(
  params: PlaybackStatisticsParams = {},
  options?: RequestOptions,
): Promise<PlaybackStatisticsResponse> {
  const query = new URLSearchParams();
  if (params.from) query.set("from", params.from);
  if (params.to) query.set("to", params.to);
  if (params.kind) query.set("kind", params.kind);
  if (params.eventKind) query.set("eventKind", params.eventKind);
  if (params.hideNsfw != null) query.set("hideNsfw", String(params.hideNsfw));
  if (params.userId) query.set("userId", params.userId);
  if (params.allUsers != null) query.set("allUsers", String(params.allUsers));

  const suffix = query.size > 0 ? `?${query.toString()}` : "";
  return fetchApi<PlaybackStatisticsResponse>(`/playback/statistics${suffix}`, {
    signal: options?.signal,
  });
}
