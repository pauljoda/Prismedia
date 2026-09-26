import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
import {
  ALIGNMENT_GAP_REASON,
  BOOK_CHAPTER_MAPPING_ORIGIN,
  BOOK_LINK_STATE,
  CONSUMPTION_MODALITY,
  type AlignmentGapReasonCode,
  type AlignmentMatchStateCode,
  type BookChapterMappingOriginCode,
} from "$lib/api/generated/codes";
import type {
  AlignedTarget,
  AudioChapterWindow,
  BookAlignmentResponse,
  BookAlignmentRow,
  BookChapterAudioMapping,
  ReadableChapterWindow,
  ReadingTarget,
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
 * Proposes the one-to-one map of the editor's "fill in order from here" step: audio windows in
 * playback order pair with readable chapters in display order, starting at the chosen chapter. The
 * proposal is shown pair by pair for review before it is used, and every row it produces remembers
 * that it was filled in order (never saved as a hand-picked pair).
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
      origin: BOOK_CHAPTER_MAPPING_ORIGIN.ordered,
      ...(window.markerId ? { audioMarkerId: window.markerId } : {}),
    }));
}

/** Reading and listening progress of a Book that keeps them separate, ready to present. */
export interface BookSeparateProgress {
  /** Whole percent (0..100) of the readable rendition before the reading position. */
  readingPercent: number;
  /** Whole percent (0..100) of the audio listened before the listening position. */
  listeningPercent: number;
  /** One-line reason the formats are not linked. */
  reason: string;
}

/** Explains why the server could not line a position up with the other format. */
export function alignmentGapExplanation(target: AlignedTarget): string | null {
  const title = target.gapChapterTitle ? `“${target.gapChapterTitle}”` : "This chapter";
  switch (target.gap) {
    case ALIGNMENT_GAP_REASON.readableChapterUnpaired:
      return `${title} has no matching audiobook chapter.`;
    case ALIGNMENT_GAP_REASON.audioChapterUnpaired:
      return `${title} has no matching ebook chapter.`;
    case ALIGNMENT_GAP_REASON.readableChaptersUnavailable:
      return "This ebook has no chapter list to line up with the audiobook.";
    case ALIGNMENT_GAP_REASON.positionOutsideChapters:
      return "Your position is outside the chapters that line up.";
    default:
      // The remaining reasons say why the whole Book keeps reading and listening separate.
      return target.gap ? bookSeparateReasonText(target.gap) : null;
  }
}

/** Short label for an exact reading position: its page, or its share of the whole book. */
export function readingPositionLabel(target: ReadingTarget | null): string | null {
  if (!target) return null;
  const total = numberValue(target.total) ?? 0;
  const pageIndex = numberValue(target.pageIndex);
  if (pageIndex !== null && total > 0) return `Page ${Math.min(pageIndex + 1, total)} of ${total}`;
  if (total <= 0) return null;
  return `${Math.round(((numberValue(target.index) ?? 0) / total) * 100)}% of book`;
}

/** One-line explanation of why a Book keeps reading and listening separate. */
export function bookSeparateReasonText(reason: AlignmentGapReasonCode | null | undefined): string {
  switch (reason) {
    case ALIGNMENT_GAP_REASON.audioUnstructured:
      return "This audiobook has no chapter markers, so reading and listening are tracked separately.";
    case ALIGNMENT_GAP_REASON.audioInParts:
      return "This audiobook is split into parts rather than chapters, so reading and listening are tracked separately.";
    case ALIGNMENT_GAP_REASON.readableChaptersUnavailable:
      return "This ebook has no chapter list to line up with the audiobook, so reading and listening are tracked separately.";
    case ALIGNMENT_GAP_REASON.noExactPairs:
      return "No chapters are paired exactly yet, so reading and listening are tracked separately. Pair chapters in Chapter Mapping to link them.";
    default:
      return "Reading and listening are tracked separately.";
  }
}

function wholePercent(value: number | string | null | undefined): number {
  const fraction = numberValue(value);
  return fraction === null ? 0 : Math.round(Math.max(0, Math.min(1, fraction)) * 100);
}

/**
 * The server's Separate decision for a Book that has both formats, or null when the Book is Linked
 * (one shared progress and switching) or has only one format. Older servers without a link decision
 * read as Linked.
 */
export function bookSeparateProgress(
  alignment: BookAlignmentResponse | null | undefined,
): BookSeparateProgress | null {
  const link = alignment?.link;
  if (!link || link.state !== BOOK_LINK_STATE.separate) return null;
  const modalities = alignment.modalities ?? [];
  if (!modalities.includes(CONSUMPTION_MODALITY.reading) || !modalities.includes(CONSUMPTION_MODALITY.listening)) {
    return null;
  }
  return {
    readingPercent: wholePercent(link.readingPercent),
    listeningPercent: wholePercent(link.listeningPercent),
    reason: bookSeparateReasonText(link.reason),
  };
}
