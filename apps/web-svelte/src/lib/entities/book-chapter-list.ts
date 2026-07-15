import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";

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

export interface BookChapterRow {
  id: string;
  title: string;
  order: number;
  depth: number;
  readTarget: BookReadTarget | null;
  readPageCount?: number | null;
  audioTrack: AudioTrackListItemDto | null;
  isCurrentReading: boolean;
  isCurrentAudio: boolean;
}

interface BuildBookChapterRowsOptions {
  readableChapters: readonly ReadableBookChapter[];
  audioTracks: readonly AudioTrackListItemDto[];
  currentReadableId?: string | null;
  currentAudioTrackId?: string | null;
}

function chapterNumber(value: string): number | null {
  const named = /\b(?:chapter|ch\.?|track|part)\s*0*(\d+)\b/i.exec(value);
  const leading = /^\s*0*(\d+)\s*(?:[.\-–—:_]|\s)/.exec(value);
  const parsed = Number(named?.[1] ?? leading?.[1]);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

/** Stable comparison key for common EPUB/audio filename chapter labels. */
export function chapterMatchKey(value: string): string {
  return value
    .normalize("NFKD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/^\s*(?:chapter|ch\.?|track|part)\s*[ivxlcdm]+\s*(?:[.\-–—:_]|\s)+/i, "")
    .replace(/^\s*(?:chapter|ch\.?|track|part)\s*0*\d+\s*(?:[.\-–—:_]|\s)*/i, "")
    .replace(/^\s*0*\d+\s*(?:[.\-–—:_]|\s)+/, "")
    .replace(/[^a-z0-9]+/g, " ")
    .trim();
}

function takeFirstMatch<T>(
  items: readonly T[],
  consumed: Set<number>,
  predicate: (item: T) => boolean,
): number | null {
  const index = items.findIndex((item, itemIndex) => !consumed.has(itemIndex) && predicate(item));
  if (index < 0) return null;
  consumed.add(index);
  return index;
}

/**
 * Builds one ordered reading/listening surface. Confident title and chapter-number matches win;
 * position is used only when every still-unmatched readable row has one remaining audio part.
 */
export function buildBookChapterRows(options: BuildBookChapterRowsOptions): BookChapterRow[] {
  const readable = [...options.readableChapters].sort(
    (a, b) => a.order - b.order || a.title.localeCompare(b.title) || a.id.localeCompare(b.id),
  );
  const tracks = [...options.audioTracks].sort(
    (a, b) => a.sortOrder - b.sortOrder || a.title.localeCompare(b.title) || a.id.localeCompare(b.id),
  );
  const consumedTracks = new Set<number>();
  const matches = new Map<string, number>();

  for (const chapter of readable) {
    const key = chapterMatchKey(chapter.title);
    if (!key) continue;
    const trackIndex = takeFirstMatch(
      tracks,
      consumedTracks,
      (track) => chapterMatchKey(track.title) === key,
    );
    if (trackIndex !== null) matches.set(chapter.id, trackIndex);
  }

  for (const chapter of readable) {
    if (matches.has(chapter.id)) continue;
    const number = chapterNumber(chapter.title);
    if (number === null) continue;
    const trackIndex = takeFirstMatch(
      tracks,
      consumedTracks,
      (track) => chapterNumber(track.title) === number,
    );
    if (trackIndex !== null) matches.set(chapter.id, trackIndex);
  }

  const unmatchedChapters = readable.filter((chapter) => !matches.has(chapter.id));
  const unmatchedTrackIndexes = tracks
    .map((_, index) => index)
    .filter((index) => !consumedTracks.has(index));
  if (unmatchedChapters.length > 0 && unmatchedChapters.length === unmatchedTrackIndexes.length) {
    unmatchedChapters.forEach((chapter, index) => {
      const trackIndex = unmatchedTrackIndexes[index]!;
      matches.set(chapter.id, trackIndex);
      consumedTracks.add(trackIndex);
    });
  }

  const rows: BookChapterRow[] = readable.map((chapter) => {
    const trackIndex = matches.get(chapter.id);
    const audioTrack = trackIndex === undefined ? null : tracks[trackIndex] ?? null;
    return {
      id: `read-${chapter.id}-${chapter.order}`,
      title: chapter.title,
      order: chapter.order,
      depth: chapter.depth,
      readTarget: chapter.target,
      readPageCount: chapter.pageCount ?? null,
      audioTrack,
      isCurrentReading: chapter.id === options.currentReadableId,
      isCurrentAudio: audioTrack?.id === options.currentAudioTrackId,
    };
  });

  tracks.forEach((track, index) => {
    if (consumedTracks.has(index)) return;
    rows.push({
      id: `audio-${track.id}`,
      title: track.title,
      order: readable.length + index,
      depth: 0,
      readTarget: null,
      readPageCount: null,
      audioTrack: track,
      isCurrentReading: false,
      isCurrentAudio: track.id === options.currentAudioTrackId,
    });
  });

  return rows;
}
