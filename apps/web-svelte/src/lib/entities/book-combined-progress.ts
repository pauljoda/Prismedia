import { BOOK_ACTIVITY_KIND, PROGRESS_UNIT, type ReaderModeCode } from "$lib/api/generated/codes";
import type { BookProgressTrackMapping, EntityCapabilityProgressCapability } from "$lib/api/generated/model";
import type { BookChapterRow } from "$lib/entities/book-chapter-list";

/** Saved book cursor normalized to both the whole readable rendition and its matched row. */
export interface BookReadingPosition {
  rowId: string;
  /** Progress across the whole canonical rendition, from 0 to 1. */
  overallFraction: number;
  /** Progress inside the matched chapter, from 0 to 1. */
  chapterFraction: number;
  /** Exact EPUB cursor when text produced the latest progress update. */
  location?: string | null;
  /** Exact zero-based page when a paged chapter produced the latest progress update. */
  pageIndex?: number | null;
}

/** Concrete reader and player coordinates derived from the one canonical book cursor. */
export interface BookCombinedLaunch {
  rowId: string;
  source: "progress" | "start";
  audioStartSeconds: number;
  readerLocation: string | null;
  readerFraction: number | null;
  readerPageIndex: number | null;
}

/** Progress payload produced by an audiobook heartbeat. */
export interface BookAudioProgressUpdate {
  currentEntityId: string;
  unit: BookProgressTrackMapping["unit"];
  index: number;
  total: number;
  mode: BookProgressTrackMapping["mode"];
  location: null;
  completed: boolean | null;
  activitySeconds: number | null;
  activityKind: typeof BOOK_ACTIVITY_KIND.listening | undefined;
}

export interface BookProgressCursor {
  currentEntityId: string;
  unit: BookProgressTrackMapping["unit"];
  index: number;
}

export interface BookAudioResumePoint {
  trackId: string;
  trackOffsetSeconds: number;
}

/** Converts the wire progress capability into the numeric cursor used by Book mapping helpers. */
export function bookProgressCursor(
  progress: EntityCapabilityProgressCapability | null | undefined,
): BookProgressCursor | null {
  if (!progress?.currentEntityId) return null;
  return {
    currentEntityId: progress.currentEntityId,
    unit: progress.unit,
    index: Number(progress.index),
  };
}

const COMBINED_AUDIO_RUNWAY_SECONDS = 5;
const EPUB_PROGRESS_TOTAL = 10_000;

function clampFraction(value: number): number {
  if (!Number.isFinite(value)) return 0;
  return Math.max(0, Math.min(1, value));
}

function roundedFraction(value: number): number {
  return Math.round(clampFraction(value) * 1_000_000) / 1_000_000;
}

function runwayStart(seconds: number): number {
  if (!Number.isFinite(seconds) || seconds <= COMBINED_AUDIO_RUNWAY_SECONDS) return 0;
  return seconds - COMBINED_AUDIO_RUNWAY_SECONDS;
}

function epubRange(row: BookChapterRow): { start: number; end: number } | null {
  if (row.readTarget?.kind !== "epub") return null;
  const start = row.readTarget.startFraction;
  const end = row.readTarget.endFraction;
  if (typeof start !== "number" || typeof end !== "number" || end <= start) return null;
  return { start, end };
}

function epubReaderFraction(row: BookChapterRow, chapterFraction: number): number | null {
  const range = epubRange(row);
  if (!range) return null;
  return roundedFraction(range.start + clampFraction(chapterFraction) * (range.end - range.start));
}

function pageReaderIndex(row: BookChapterRow, chapterFraction: number): number | null {
  if (row.readTarget?.kind !== "entity-chapter") return null;
  const pageCount = Math.max(0, Math.floor(row.readPageCount ?? 0));
  if (pageCount <= 0) return null;
  const inferredPage = Math.ceil(clampFraction(chapterFraction) * pageCount) - 1;
  return Math.max(0, Math.min(pageCount - 1, inferredPage));
}

/** Converts the whole-book EPUB cursor into a position inside a matched TOC chapter. */
export function epubChapterFraction(row: BookChapterRow, overallFraction: number): number {
  const range = epubRange(row);
  if (!range) return 0;
  return clampFraction((clampFraction(overallFraction) - range.start) / (range.end - range.start));
}

/** Aligns the reader and audiobook coordinates for one matched chapter. */
export function resolveChapterCombinedLaunch(
  row: BookChapterRow,
  position?: BookReadingPosition | null,
): BookCombinedLaunch | null {
  if (!row.readTarget || !row.audioTrack) return null;

  const rowPosition = position?.rowId === row.id ? position : null;
  const chapterFraction = clampFraction(rowPosition?.chapterFraction ?? 0);
  const duration = Math.max(0, Number(row.audioTrack.duration ?? 0));

  return {
    rowId: row.id,
    source: rowPosition ? "progress" : "start",
    audioStartSeconds: runwayStart(chapterFraction * duration),
    readerLocation: rowPosition?.location ?? null,
    readerFraction: rowPosition?.location ? null : epubReaderFraction(row, chapterFraction),
    readerPageIndex: rowPosition?.pageIndex ?? pageReaderIndex(row, chapterFraction),
  };
}

/** Resolves the matched row owned by the one whole-book cursor. */
export function resolveBookCombinedResume(
  rows: readonly BookChapterRow[],
  position?: BookReadingPosition | null,
): BookCombinedLaunch | null {
  const matchedRows = rows.filter((row) => row.readTarget && row.audioTrack);
  if (matchedRows.length === 0) return null;
  const row = position
    ? matchedRows.find((candidate) => candidate.id === position.rowId) ?? matchedRows[0]!
    : matchedRows[0]!;
  return resolveChapterCombinedLaunch(row, position?.rowId === row.id ? position : null);
}

/**
 * Creates chapter-scoped audio-to-text mappings. Unmatched audio is deliberately ignored when a
 * readable rendition exists; for audio-only books, seconds form the canonical progress unit.
 */
export function buildBookProgressMappings(
  bookId: string,
  rows: readonly BookChapterRow[],
  mode: ReaderModeCode | null,
): BookProgressTrackMapping[] {
  const hasReadableRendition = rows.some((row) => row.readTarget !== null);

  if (!hasReadableRendition) {
    const durations = rows.map((row) => Math.max(0, Math.ceil(Number(row.audioTrack?.duration ?? 0))));
    const total = durations.reduce((sum, duration) => sum + duration, 0);
    let startIndex = 0;
    return rows.flatMap((row, rowIndex) => {
      const duration = durations[rowIndex] ?? 0;
      if (!row.audioTrack || duration <= 0 || total <= 0) return [];
      const mapping = {
        trackId: row.audioTrack.id,
        currentEntityId: bookId,
        unit: PROGRESS_UNIT.second,
        startIndex,
        endIndex: startIndex + duration,
        total,
        mode: null,
      } satisfies BookProgressTrackMapping;
      startIndex += duration;
      return [mapping];
    });
  }

  return rows.flatMap((row): BookProgressTrackMapping[] => {
    if (!row.audioTrack || !row.readTarget) return [];

    if (row.readTarget.kind === "epub") {
      const range = epubRange(row);
      if (!range) return [];
      return [{
        trackId: row.audioTrack.id,
        currentEntityId: bookId,
        unit: PROGRESS_UNIT.cfi,
        startIndex: Math.round(range.start * EPUB_PROGRESS_TOTAL),
        endIndex: Math.round(range.end * EPUB_PROGRESS_TOTAL),
        total: EPUB_PROGRESS_TOTAL,
        mode,
      }];
    }

    const pageCount = Math.max(0, Math.floor(row.readPageCount ?? 0));
    if (pageCount <= 0) return [];
    return [{
      trackId: row.audioTrack.id,
      currentEntityId: row.readTarget.chapterId,
      unit: PROGRESS_UNIT.page,
      startIndex: 0,
      endIndex: pageCount - 1,
      total: pageCount,
      mode,
    }];
  });
}

/** Converts one local audio position into the canonical chapter-scoped book cursor. */
export function bookProgressUpdateForAudio(
  mapping: BookProgressTrackMapping,
  offsetSeconds: number,
  durationSeconds: number,
  activitySeconds: number | null,
  completed: boolean,
): BookAudioProgressUpdate {
  const start = Number(mapping.startIndex);
  const end = Number(mapping.endIndex);
  const total = Number(mapping.total);
  const fraction = durationSeconds > 0 ? clampFraction(offsetSeconds / durationSeconds) : 0;
  let index: number;

  if (mapping.unit === PROGRESS_UNIT.page) {
    index = Math.max(start, Math.min(end, Math.ceil(fraction * total) - 1));
  } else {
    index = Math.max(start, Math.min(end, Math.round(start + fraction * (end - start))));
  }

  return {
    currentEntityId: mapping.currentEntityId,
    unit: mapping.unit,
    index,
    total,
    mode: mapping.mode,
    location: null,
    completed: completed ? true : null,
    activitySeconds,
    activityKind: activitySeconds && activitySeconds > 0
      ? BOOK_ACTIVITY_KIND.listening
      : undefined,
  };
}

/** Maps the canonical Book cursor back into its matched audiobook part. */
export function resolveBookAudioResume(
  rows: readonly BookChapterRow[],
  mappings: readonly BookProgressTrackMapping[],
  cursor: BookProgressCursor | null | undefined,
): BookAudioResumePoint | null {
  if (!cursor) return null;
  const candidates = mappings.filter((mapping) =>
    mapping.currentEntityId === cursor.currentEntityId && mapping.unit === cursor.unit
  );
  const mapping = candidates.find((candidate) => {
    const start = Number(candidate.startIndex);
    const end = Number(candidate.endIndex);
    return cursor.index >= start && cursor.index <= end;
  }) ?? candidates.at(-1) ?? null;
  if (!mapping) return null;

  const track = rows.find((row) => row.audioTrack?.id === mapping.trackId)?.audioTrack;
  if (!track) return null;
  const start = Number(mapping.startIndex);
  const end = Number(mapping.endIndex);
  const fraction = end > start ? clampFraction((cursor.index - start) / (end - start)) : 0;
  return {
    trackId: mapping.trackId,
    trackOffsetSeconds: runwayStart(fraction * Math.max(0, Number(track.duration ?? 0))),
  };
}
