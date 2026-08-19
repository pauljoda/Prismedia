import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
import type { BookChapterAudioMapping } from "$lib/api/generated/model";

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
  chapterMappings?: readonly BookChapterAudioMapping[];
  currentReadableId?: string | null;
  currentAudioTrackId?: string | null;
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
 * Builds one ordered reading/listening surface. Persisted user mappings win, then normalized title
 * text can supply safe automatic matches. Numbers and sort order never determine chapter identity.
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

  const readableIds = new Set(readable.map((chapter) => chapter.id));
  const trackIndexById = new Map(tracks.map((track, index) => [track.id, index]));
  for (const mapping of options.chapterMappings ?? []) {
    if (!readableIds.has(mapping.readableChapterKey) || matches.has(mapping.readableChapterKey)) {
      continue;
    }
    const trackIndex = trackIndexById.get(mapping.audioTrackId);
    if (trackIndex === undefined || consumedTracks.has(trackIndex)) continue;
    consumedTracks.add(trackIndex);
    matches.set(mapping.readableChapterKey, trackIndex);
  }

  for (const chapter of readable) {
    if (matches.has(chapter.id)) continue;
    const key = chapterMatchKey(chapter.title);
    if (!key) continue;
    const trackIndex = takeFirstMatch(
      tracks,
      consumedTracks,
      (track) => chapterMatchKey(track.title) === key,
    );
    if (trackIndex !== null) matches.set(chapter.id, trackIndex);
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

/** Creates the editable one-to-one map produced by the "Mark first chapter" workflow. */
export function sequentialBookChapterMappings(
  readableChapters: readonly ReadableBookChapter[],
  audioTracks: readonly AudioTrackListItemDto[],
  firstReadableChapterKey: string,
): BookChapterAudioMapping[] {
  const readable = [...readableChapters].sort(
    (a, b) => a.order - b.order || a.title.localeCompare(b.title) || a.id.localeCompare(b.id),
  );
  const tracks = [...audioTracks].sort(
    (a, b) => a.sortOrder - b.sortOrder || a.title.localeCompare(b.title) || a.id.localeCompare(b.id),
  );
  const firstIndex = readable.findIndex((chapter) => chapter.id === firstReadableChapterKey);
  if (firstIndex < 0) return [];

  return tracks
    .slice(0, Math.max(0, readable.length - firstIndex))
    .map((track, index) => ({
      readableChapterKey: readable[firstIndex + index].id,
      audioTrackId: track.id,
    }));
}
