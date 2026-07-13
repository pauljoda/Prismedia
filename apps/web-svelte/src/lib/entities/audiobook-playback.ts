import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
import type { EntityCard } from "$lib/api/generated/model";
import { ENTITY_KIND } from "$lib/entities/entity-codes";
import { entityThumbnailToTrackItem } from "$lib/entities/audio-track-items";
import { orderedBookChildren } from "$lib/entities/book-entity-reader";

export interface AudiobookResumePoint {
  trackId: string;
  trackOffsetSeconds: number;
}

function trackDuration(
  track: AudioTrackListItemDto,
  runtimeDurations?: ReadonlyMap<string, number>,
): number {
  const duration = Number(track.duration ?? 0);
  if (Number.isFinite(duration) && duration > 0) return duration;

  const runtimeDuration = Number(runtimeDurations?.get(track.id) ?? 0);
  return Number.isFinite(runtimeDuration) && runtimeDuration > 0 ? runtimeDuration : 0;
}

/** Maps a Book's ordered audio-track children through the same adapter used by album queues. */
export function audiobookTrackItems(
  book: Pick<EntityCard, "id" | "childrenByKind">,
): AudioTrackListItemDto[] {
  return orderedBookChildren(book, ENTITY_KIND.audioTrack).map((thumbnail) =>
    entityThumbnailToTrackItem(thumbnail, book.id, { libraryId: book.id }),
  );
}

/** Total known runtime, including browser-learned durations for parts awaiting a probe. */
export function audiobookDuration(
  tracks: readonly AudioTrackListItemDto[],
  runtimeDurations?: ReadonlyMap<string, number>,
): number {
  return tracks.reduce((total, track) => total + trackDuration(track, runtimeDurations), 0);
}

/**
 * Converts a Book-level absolute resume position into the concrete audio track and local timestamp
 * the shared audio element needs. Unknown-duration queues start from the first part safely.
 */
export function resolveAudiobookResume(
  tracks: readonly AudioTrackListItemDto[],
  absoluteSeconds: number,
): AudiobookResumePoint | null {
  const first = tracks[0];
  if (!first) return null;

  const totalDuration = audiobookDuration(tracks);
  if (totalDuration <= 0) return { trackId: first.id, trackOffsetSeconds: 0 };

  let remaining = Math.max(0, Math.min(absoluteSeconds, totalDuration));
  for (const [index, track] of tracks.entries()) {
    const duration = trackDuration(track);
    const isLast = index === tracks.length - 1;
    if (remaining < duration || isLast) {
      return {
        trackId: track.id,
        trackOffsetSeconds: Math.max(0, Math.min(remaining, duration)),
      };
    }
    remaining -= duration;
  }

  return { trackId: first.id, trackOffsetSeconds: 0 };
}

/** Converts a concrete part timestamp back into the Book-level absolute playback position. */
export function audiobookAbsoluteTime(
  tracks: readonly AudioTrackListItemDto[],
  trackId: string,
  trackOffsetSeconds: number,
  runtimeDurations?: ReadonlyMap<string, number>,
): number {
  const trackIndex = tracks.findIndex((track) => track.id === trackId);
  if (trackIndex < 0) return 0;

  const elapsedBeforeTrack = tracks
    .slice(0, trackIndex)
    .reduce((total, track) => total + trackDuration(track, runtimeDurations), 0);
  const duration = trackDuration(tracks[trackIndex]!, runtimeDurations);
  const localOffset = Math.max(0, Math.min(trackOffsetSeconds, duration || trackOffsetSeconds));
  return elapsedBeforeTrack + localOffset;
}
