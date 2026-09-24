import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
import type {
  AlignmentMatchStateCode,
  BookChapterMappingOriginCode,
} from "$lib/api/generated/codes";
import type {
  AudioChapterWindow,
  BookAlignmentResponse,
  BookAlignmentRow,
  BookChapterAudioMapping,
  ReadableChapterWindow,
} from "$lib/api/generated/model";

export type BookReadTarget =
  | {
      kind: "epub";
      location: string;
      startFraction?: number | null;
      endFraction?: number | null;
    }
  | { kind: "entity-chapter"; chapterId: string };

export interface ReadableBookChapter {
  id: string;
  title: string;
  order: number;
  depth: number;
  target: BookReadTarget;
  pageCount?: number | null;
}

/** Presentation row for one server-projected alignment row. */
export interface BookChapterRow {
  id: string;
  title: string;
  order: number;
  depth: number;
  matchState?: AlignmentMatchStateCode;
  provenance?: BookChapterMappingOriginCode | null;
  readTarget: BookReadTarget | null;
  readPageCount?: number | null;
  audioTrack: AudioTrackListItemDto | null;
  audioMarkerId?: string | null;
  audioStartSeconds?: number | null;
  audioEndSeconds?: number | null;
  isCurrentReading: boolean;
  isCurrentAudio: boolean;
}

/** One audio chapter window of the alignment with its stable editor key. */
export interface BookAudioWindowEntry {
  key: string;
  window: AudioChapterWindow;
}

interface BookChapterRowsOptions {
  alignment: BookAlignmentResponse | null | undefined;
  audioTracks: readonly AudioTrackListItemDto[];
  /** Row holding the resumable reading position, from the server's resume projection. */
  readingRowId?: string | null;
  /** Row holding the resumable listening position, used while this Book is not playing. */
  listeningRowId?: string | null;
  /** Live player position, when this Book's audio is the current queue. */
  playingTrackId?: string | null;
  playingSeconds?: number | null;
}

function numberValue(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined) return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

/** Stable key of one whole-track or embedded-marker audio window. */
export function audioWindowKey(trackId: string, markerId: string | null | undefined): string {
  return `${trackId}:${markerId ?? "whole"}`;
}

function readTarget(row: BookAlignmentRow): BookReadTarget | null {
  const readable = row.readable;
  if (!readable) return null;
  if (readable.chapterEntityId) return { kind: "entity-chapter", chapterId: readable.chapterEntityId };
  return {
    kind: "epub",
    location: readable.location ?? readable.chapterKey,
    startFraction: numberValue(readable.startFraction),
    endFraction: numberValue(readable.endFraction),
  };
}

function windowContains(
  window: Pick<AudioChapterWindow, "trackEntityId" | "startSeconds" | "endSeconds">,
  trackId: string | null | undefined,
  seconds: number | null | undefined,
): boolean {
  if (window.trackEntityId !== trackId || seconds === null || seconds === undefined || !Number.isFinite(seconds)) {
    return false;
  }
  const end = numberValue(window.endSeconds);
  return seconds >= (numberValue(window.startSeconds) ?? 0) && (end === null || seconds < end);
}

/**
 * Presents the server's alignment rows in their projected order. The rows, their pairing, and the
 * rows holding each saved position all come from the server; only the live player highlight is
 * computed here.
 */
export function bookChapterRowsFromAlignment(options: BookChapterRowsOptions): BookChapterRow[] {
  const trackById = new Map(options.audioTracks.map((track) => [track.id, track]));
  const playing = options.playingTrackId != null;
  return (options.alignment?.rows ?? []).map((row) => {
    const audio = row.audio ?? null;
    return {
      id: row.rowId,
      title: row.readable?.title ?? audio?.title ?? "",
      order: numberValue(row.order) ?? 0,
      depth: numberValue(row.readable?.depth) ?? 0,
      matchState: row.matchState,
      provenance: row.provenance ?? null,
      readTarget: readTarget(row),
      readPageCount: numberValue(row.readable?.pageCount),
      audioTrack: audio ? trackById.get(audio.trackEntityId) ?? null : null,
      audioMarkerId: audio?.markerId ?? null,
      audioStartSeconds: audio ? numberValue(audio.startSeconds) : null,
      audioEndSeconds: audio ? numberValue(audio.endSeconds) : null,
      isCurrentReading: options.readingRowId != null && row.rowId === options.readingRowId,
      isCurrentAudio: playing
        ? audio !== null && windowContains(audio, options.playingTrackId, options.playingSeconds)
        : options.listeningRowId != null && row.rowId === options.listeningRowId,
    };
  });
}

/** Readable chapters of the alignment, in display order, as reader launch targets. */
export function readableChaptersFromAlignment(
  alignment: BookAlignmentResponse | null | undefined,
): ReadableBookChapter[] {
  return (alignment?.rows ?? []).flatMap((row, order) => {
    const target = readTarget(row);
    return row.readable && target
      ? [{
          id: row.readable.chapterKey,
          title: row.readable.title,
          order,
          depth: numberValue(row.readable.depth) ?? 0,
          target,
          pageCount: numberValue(row.readable.pageCount),
        }]
      : [];
  });
}

/** Paired alignment rows expressed as persisted chapter mappings, with their provenance. */
export function alignmentChapterMappings(
  alignment: BookAlignmentResponse | null | undefined,
): BookChapterAudioMapping[] {
  return (alignment?.rows ?? []).flatMap((row) =>
    row.readable && row.audio
      ? [{
          readableChapterKey: row.readable.chapterKey,
          audioTrackId: row.audio.trackEntityId,
          audioMarkerId: row.audio.markerId ?? null,
          origin: row.provenance ?? null,
        }]
      : []);
}

/** Readable chapter windows of the alignment, in display order. */
export function alignmentReadableWindows(
  alignment: BookAlignmentResponse | null | undefined,
): ReadableChapterWindow[] {
  return (alignment?.rows ?? []).flatMap((row) => row.readable ? [row.readable] : []);
}

/** Audio chapter windows of the alignment in playback order: track order, then start time. */
export function alignmentAudioWindows(
  alignment: BookAlignmentResponse | null | undefined,
  audioTracks: readonly AudioTrackListItemDto[],
): BookAudioWindowEntry[] {
  const trackOrder = new Map([...audioTracks]
    .sort((a, b) => a.sortOrder - b.sortOrder || a.title.localeCompare(b.title) || a.id.localeCompare(b.id))
    .map((track, index) => [track.id, index]));
  return (alignment?.rows ?? [])
    .flatMap((row) => row.audio ? [row.audio] : [])
    .sort((a, b) =>
      (trackOrder.get(a.trackEntityId) ?? Number.MAX_SAFE_INTEGER)
        - (trackOrder.get(b.trackEntityId) ?? Number.MAX_SAFE_INTEGER)
        || (numberValue(a.startSeconds) ?? 0) - (numberValue(b.startSeconds) ?? 0))
    .map((window) => ({ key: audioWindowKey(window.trackEntityId, window.markerId), window }));
}

/** Checks whether a physical audiobook position belongs to this row's audio window. */
export function bookChapterRowOwnsAudioTime(
  row: BookChapterRow,
  trackId: string | null | undefined,
  seconds: number | null | undefined,
): boolean {
  if (!row.audioTrack) return false;
  return windowContains(
    {
      trackEntityId: row.audioTrack.id,
      startSeconds: row.audioStartSeconds ?? 0,
      endSeconds: row.audioEndSeconds ?? null,
    },
    trackId,
    seconds,
  );
}

/**
 * Creates the editable one-to-one map produced by the "Mark first chapter" workflow: audio windows in
 * playback order pair with readable chapters in display order, starting at the marked chapter.
 */
export function sequentialBookChapterMappings(
  readableWindows: readonly ReadableChapterWindow[],
  audioWindows: readonly BookAudioWindowEntry[],
  firstReadableChapterKey: string,
): BookChapterAudioMapping[] {
  const firstIndex = readableWindows.findIndex((chapter) => chapter.chapterKey === firstReadableChapterKey);
  if (firstIndex < 0) return [];

  return audioWindows
    .slice(0, Math.max(0, readableWindows.length - firstIndex))
    .map(({ window }, index) => ({
      readableChapterKey: readableWindows[firstIndex + index].chapterKey,
      audioTrackId: window.trackEntityId,
      ...(window.markerId ? { audioMarkerId: window.markerId } : {}),
    }));
}
