import type { AudioTrackChapter, AudioTrackListItemDto } from "$lib/entities/media-view-models";

/** Seconds into a chapter after which Previous restarts it instead of stepping back a chapter. */
export const CHAPTER_RESTART_THRESHOLD_SECONDS = 3;

/**
 * One embedded chapter as the span of its file it plays as. Spans are contiguous: the first starts
 * at 0, each ends where the next begins, and the last runs to the end of the file, so every instant
 * of the file belongs to exactly one chapter.
 */
export interface AudioChapterSpan {
  markerId: string;
  title: string;
  /** Zero-based position among the file's chapters. */
  index: number;
  startSeconds: number;
  /** Span end; null only for the last chapter while the file's duration is unknown. */
  endSeconds: number | null;
}

function finitePositive(value: number | null | undefined): number | null {
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

/** Chapters in start order inside the file, one per start time. */
function orderedChapters(chapters: readonly AudioTrackChapter[], duration: number | null): AudioTrackChapter[] {
  const ordered = chapters
    .map((chapter) => ({ ...chapter, startSeconds: Math.max(0, Number(chapter.startSeconds) || 0) }))
    .filter((chapter) => duration === null || chapter.startSeconds < duration)
    .sort((a, b) => a.startSeconds - b.startSeconds);
  return ordered.filter((chapter, index) =>
    index === 0 || chapter.startSeconds !== ordered[index - 1]!.startSeconds);
}

/**
 * The spans a file's embedded chapters play as. A file with fewer than two chapters plays whole, so
 * it has no spans. `durationSeconds` is the file's known length, preferred over a declared last end.
 */
export function audioChapterSpans(
  track: Pick<AudioTrackListItemDto, "chapters" | "duration"> | null | undefined,
  durationSeconds?: number | null,
): AudioChapterSpan[] {
  const duration = finitePositive(durationSeconds) ?? finitePositive(track?.duration);
  const chapters = orderedChapters(track?.chapters ?? [], duration);
  if (chapters.length < 2) return [];

  return chapters.map((chapter, index) => {
    const next = chapters[index + 1];
    return {
      markerId: chapter.markerId,
      title: chapter.title,
      index,
      startSeconds: index === 0 ? 0 : chapter.startSeconds,
      endSeconds: next ? next.startSeconds : duration ?? finitePositive(chapter.endSeconds),
    };
  });
}

/** The span playing at `seconds` of its file; the first span before any chapter begins. */
export function audioChapterSpanAt(
  spans: readonly AudioChapterSpan[],
  seconds: number,
): AudioChapterSpan | null {
  if (spans.length === 0) return null;
  const position = Number.isFinite(seconds) ? seconds : 0;
  let current = spans[0]!;
  for (const span of spans) {
    if (span.startSeconds > position) break;
    current = span;
  }
  return current;
}

/**
 * Length of, and position inside, the entry playing at `fileSeconds` of a file `fileDuration` long:
 * its chapter, else the whole file. A chapter without a known end runs to the end of the file.
 */
export function audioEntryPosition(
  spans: readonly AudioChapterSpan[],
  fileSeconds: number,
  fileDuration: number,
): { start: number; duration: number; position: number } {
  const chapter = audioChapterSpanAt(spans, fileSeconds);
  const start = chapter?.startSeconds ?? 0;
  const end = chapter?.endSeconds ?? fileDuration;
  const duration = end > start ? end - start : 0;
  return { start, duration, position: Math.max(0, Math.min(fileSeconds - start, duration || Number.POSITIVE_INFINITY)) };
}

/** Start of the last chapter of a file, where stepping back into the file begins; 0 when it plays whole. */
export function lastAudioChapterStart(
  track: Pick<AudioTrackListItemDto, "chapters" | "duration"> | null | undefined,
): number {
  return audioChapterSpans(track).at(-1)?.startSeconds ?? 0;
}
