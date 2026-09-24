import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
import type {
  AlignmentMatchStateCode,
  BookChapterMappingOriginCode,
} from "$lib/api/generated/codes";
import type {
  AudioChapterWindow,
  BookAlignmentResponse,
  BookAlignmentRow,
  BookAudioChapter,
  BookChapterAudioMapping,
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

export interface BookAudioChapterCandidate {
  key: string;
  track: AudioTrackListItemDto;
  markerId: string | null;
  title: string;
  startSeconds: number;
  endSeconds: number | null;
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

function candidateKey(trackId: string, markerId: string | null | undefined): string {
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

/** Every audio chapter window of the alignment. */
export function alignmentAudioChapters(
  alignment: BookAlignmentResponse | null | undefined,
): BookAudioChapter[] {
  return (alignment?.rows ?? []).flatMap((row) =>
    row.audio
      ? [{
          audioTrackId: row.audio.trackEntityId,
          audioMarkerId: row.audio.markerId ?? null,
          title: row.audio.title,
          startSeconds: row.audio.startSeconds,
          endSeconds: row.audio.endSeconds ?? null,
        }]
      : []);
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

/** Expands each physical audiobook file into its addressable whole-track or embedded chapters. */
export function bookAudioChapterCandidates(
  audioTracks: readonly AudioTrackListItemDto[],
  audioChapters: readonly BookAudioChapter[] = [],
): BookAudioChapterCandidate[] {
  const tracks = [...audioTracks].sort(
    (a, b) => a.sortOrder - b.sortOrder || a.title.localeCompare(b.title) || a.id.localeCompare(b.id),
  );
  const trackById = new Map(tracks.map((track) => [track.id, track]));
  if (audioChapters.length === 0) {
    return tracks.map((track) => ({
      key: candidateKey(track.id, null),
      track,
      markerId: null,
      title: track.title,
      startSeconds: 0,
      endSeconds: numberValue(track.duration),
    }));
  }

  return audioChapters.flatMap((chapter) => {
    const track = trackById.get(chapter.audioTrackId);
    if (!track) return [];
    const startSeconds = numberValue(chapter.startSeconds) ?? 0;
    return [{
      key: candidateKey(track.id, chapter.audioMarkerId),
      track,
      markerId: chapter.audioMarkerId,
      title: chapter.title,
      startSeconds,
      endSeconds: numberValue(chapter.endSeconds),
    }];
  }).sort((a, b) =>
    a.track.sortOrder - b.track.sortOrder
      || a.startSeconds - b.startSeconds
      || a.title.localeCompare(b.title)
      || a.key.localeCompare(b.key)
  );
}

/** Creates the editable one-to-one map produced by the "Mark first chapter" workflow. */
export function sequentialBookChapterMappings(
  readableChapters: readonly ReadableBookChapter[],
  audioTracks: readonly AudioTrackListItemDto[],
  firstReadableChapterKey: string,
  audioChapters: readonly BookAudioChapter[] = [],
): BookChapterAudioMapping[] {
  const readable = [...readableChapters].sort(
    (a, b) => a.order - b.order || a.title.localeCompare(b.title) || a.id.localeCompare(b.id),
  );
  const candidates = bookAudioChapterCandidates(audioTracks, audioChapters);
  const firstIndex = readable.findIndex((chapter) => chapter.id === firstReadableChapterKey);
  if (firstIndex < 0) return [];

  return candidates
    .slice(0, Math.max(0, readable.length - firstIndex))
    .map((candidate, index) => ({
      readableChapterKey: readable[firstIndex + index].id,
      audioTrackId: candidate.track.id,
      ...(candidate.markerId ? { audioMarkerId: candidate.markerId } : {}),
    }));
}
