import type { AudioTrackListItemDto } from "@prismedia/contracts";
import {
  clearMusicPlayerState,
  getMusicPlayerState,
  updateMusicPlayerState,
} from "$lib/api/generated/prismedia";
import type {
  MusicPlayerStateResponse,
  UpdateMusicPlayerStateRequest,
} from "$lib/api/generated/model";
import { audioTrackDetailToListItem } from "$lib/entities/audio-track-items";
import type { MiniPlayerSide, PlaybackContext, RepeatMode } from "$lib/stores/audio-playback.svelte";

export interface RestoredMusicPlayerState {
  queue: AudioTrackListItemDto[];
  order: number[];
  position: number;
  playing: boolean;
  shuffle: boolean;
  repeat: RepeatMode;
  volume: number;
  muted: boolean;
  collapsed: boolean;
  collapsedSide: MiniPlayerSide;
  context: PlaybackContext | null;
}

export interface PersistMusicPlayerState {
  queueTrackIds: string[];
  order: number[];
  position: number;
  playing: boolean;
  shuffle: boolean;
  repeat: RepeatMode;
  volume: number;
  muted: boolean;
  collapsed: boolean;
  collapsedSide: MiniPlayerSide;
  context: PlaybackContext | null;
}

function numberValue(value: number | string): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

function albumCoverUrlsForWire(
  urls: PlaybackContext["albumCoverUrls"],
): Record<string, string> | null {
  if (!urls) return null;
  const entries = Object.entries(urls).filter((entry): entry is [string, string] => typeof entry[1] === "string");
  return entries.length > 0 ? Object.fromEntries(entries) : null;
}

function fromResponse(response: MusicPlayerStateResponse): RestoredMusicPlayerState {
  return {
    queue: response.tracks.map(audioTrackDetailToListItem),
    order: response.order.map(numberValue),
    position: numberValue(response.position),
    playing: response.playing,
    shuffle: response.shuffle,
    repeat: response.repeat,
    volume: numberValue(response.volume),
    muted: response.muted,
    collapsed: response.collapsed,
    collapsedSide: response.collapsedSide,
    context: response.context
      ? {
          albumId: response.context.albumId,
          albumTitle: response.context.albumTitle,
          artistId: response.context.artistId,
          artistName: response.context.artistName,
          coverUrl: response.context.coverUrl,
          albumCoverUrls: response.context.albumCoverUrls,
        }
      : null,
  };
}

function toRequest(state: PersistMusicPlayerState): UpdateMusicPlayerStateRequest {
  return {
    queueTrackIds: state.queueTrackIds,
    order: state.order,
    position: state.position,
    playing: state.playing,
    shuffle: state.shuffle,
    repeat: state.repeat,
    volume: state.volume,
    muted: state.muted,
    collapsed: state.collapsed,
    collapsedSide: state.collapsedSide,
    context: state.context
      ? {
          albumId: state.context.albumId ?? null,
          albumTitle: state.context.albumTitle ?? null,
          artistId: state.context.artistId ?? null,
          artistName: state.context.artistName ?? null,
          coverUrl: state.context.coverUrl ?? null,
          albumCoverUrls: albumCoverUrlsForWire(state.context.albumCoverUrls),
        }
      : null,
  };
}

export async function fetchMusicPlayerState(signal?: AbortSignal): Promise<RestoredMusicPlayerState> {
  const response = await getMusicPlayerState({ signal });
  return fromResponse(response.data);
}

export async function saveMusicPlayerState(state: PersistMusicPlayerState): Promise<void> {
  if (state.queueTrackIds.length === 0) {
    await clearMusicPlayerState();
    return;
  }

  await updateMusicPlayerState(toRequest(state));
}

export async function clearPersistedMusicPlayerState(): Promise<void> {
  await clearMusicPlayerState();
}
