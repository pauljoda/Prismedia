import type { BookChapterRow } from "$lib/entities/book-chapter-list";

/** Saved reading cursor normalized to both the whole book and its matched chapter row. */
export interface BookReadingPosition {
  rowId: string;
  /** Progress across the whole readable rendition, from 0 to 1. */
  overallFraction: number;
  /** Progress inside the matched chapter, from 0 to 1. */
  chapterFraction: number;
  /** Exact EPUB cursor when reading is the leading rendition. */
  location?: string | null;
  /** Exact zero-based page when reading is the leading rendition. */
  pageIndex?: number | null;
}

/** Saved listening cursor normalized to both the whole audiobook and its matched chapter row. */
export interface BookListeningPosition {
  rowId: string;
  /** Progress across the whole audiobook rendition, from 0 to 1. */
  overallFraction: number;
  /** Progress inside the matched audio part, from 0 to 1. */
  chapterFraction: number;
  /** Exact timestamp inside the matched audio part. */
  trackOffsetSeconds: number;
}

/** Concrete reader and player coordinates for one coordinated launch. */
export interface BookCombinedLaunch {
  rowId: string;
  source: "reading" | "listening" | "start";
  audioStartSeconds: number;
  readerLocation: string | null;
  readerFraction: number | null;
  readerPageIndex: number | null;
}

const COMBINED_AUDIO_RUNWAY_SECONDS = 5;

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

function matchingReading(
  row: BookChapterRow,
  position: BookReadingPosition | null | undefined,
): BookReadingPosition | null {
  return position?.rowId === row.id ? position : null;
}

function matchingListening(
  row: BookChapterRow,
  position: BookListeningPosition | null | undefined,
): BookListeningPosition | null {
  return position?.rowId === row.id ? position : null;
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
  return roundedFraction(
    range.start + clampFraction(chapterFraction) * (range.end - range.start),
  );
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
  return clampFraction(
    (clampFraction(overallFraction) - range.start) / (range.end - range.start),
  );
}

/**
 * Aligns one matched chapter. The farther cursor inside the row leads; audio starts five seconds
 * before the inferred sync point so the user has a short runway to line up the text manually.
 */
export function resolveChapterCombinedLaunch(
  row: BookChapterRow,
  reading?: BookReadingPosition | null,
  listening?: BookListeningPosition | null,
): BookCombinedLaunch | null {
  if (!row.readTarget || !row.audioTrack) return null;

  const rowReading = matchingReading(row, reading);
  const rowListening = matchingListening(row, listening);
  const listeningLeads = Boolean(
    rowListening && (!rowReading || rowListening.chapterFraction > rowReading.chapterFraction),
  );
  const source = listeningLeads ? "listening" : rowReading ? "reading" : "start";
  const chapterFraction = listeningLeads
    ? clampFraction(rowListening?.chapterFraction ?? 0)
    : clampFraction(rowReading?.chapterFraction ?? 0);
  const duration = Math.max(0, Number(row.audioTrack.duration ?? 0));
  const syncSeconds = listeningLeads
    ? Math.max(0, Number(rowListening?.trackOffsetSeconds ?? 0))
    : chapterFraction * duration;

  return {
    rowId: row.id,
    source,
    audioStartSeconds: runwayStart(syncSeconds),
    readerLocation: source === "reading" ? rowReading?.location ?? null : null,
    readerFraction: source === "listening" ? epubReaderFraction(row, chapterFraction) : null,
    readerPageIndex: source === "reading"
      ? rowReading?.pageIndex ?? null
      : source === "listening"
        ? pageReaderIndex(row, chapterFraction)
        : null,
  };
}

/** Chooses the farther active rendition across the whole book, then aligns its matched row. */
export function resolveBookCombinedResume(
  rows: readonly BookChapterRow[],
  reading?: BookReadingPosition | null,
  listening?: BookListeningPosition | null,
): BookCombinedLaunch | null {
  const matchedRows = rows.filter((row) => row.readTarget && row.audioTrack);
  if (matchedRows.length === 0) return null;

  const readingRow = reading
    ? matchedRows.find((row) => row.id === reading.rowId) ?? null
    : null;
  const listeningRow = listening
    ? matchedRows.find((row) => row.id === listening.rowId) ?? null
    : null;
  const listeningLeads = Boolean(
    listeningRow &&
      (!readingRow ||
        clampFraction(listening?.overallFraction ?? 0) >
          clampFraction(reading?.overallFraction ?? 0)),
  );
  const row = listeningLeads ? listeningRow : readingRow ?? listeningRow ?? matchedRows[0]!;
  if (!row) return null;

  return resolveChapterCombinedLaunch(
    row,
    reading?.rowId === row.id ? reading : null,
    listening?.rowId === row.id ? listening : null,
  );
}
