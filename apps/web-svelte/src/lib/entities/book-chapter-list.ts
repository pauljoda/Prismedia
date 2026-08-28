import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
import type { BookAudioChapter, BookChapterAudioMapping } from "$lib/api/generated/model";

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
  audioMarkerId?: string | null;
  audioStartSeconds?: number | null;
  audioEndSeconds?: number | null;
  isCurrentReading: boolean;
  isCurrentAudio: boolean;
}

interface BuildBookChapterRowsOptions {
  readableChapters: readonly ReadableBookChapter[];
  audioTracks: readonly AudioTrackListItemDto[];
  audioChapters?: readonly BookAudioChapter[];
  chapterMappings?: readonly BookChapterAudioMapping[];
  currentReadableId?: string | null;
  currentAudioTrackId?: string | null;
  currentAudioSeconds?: number | null;
}

export interface BookAudioChapterCandidate {
  key: string;
  track: AudioTrackListItemDto;
  markerId: string | null;
  title: string;
  startSeconds: number;
  endSeconds: number | null;
}

function numberValue(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined) return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function candidateKey(trackId: string, markerId: string | null | undefined): string {
  return `${trackId}:${markerId ?? "whole"}`;
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

function isCurrentAudioCandidate(
  candidate: BookAudioChapterCandidate,
  trackId: string | null | undefined,
  seconds: number | null | undefined,
): boolean {
  if (candidate.track.id !== trackId) return false;
  if (candidate.markerId === null) return true;
  if (seconds === null || seconds === undefined || !Number.isFinite(seconds)) return false;
  return seconds >= candidate.startSeconds &&
    (candidate.endSeconds === null || seconds < candidate.endSeconds);
}

/**
 * Builds one ordered reading/listening surface from the server-persisted chapter map. The map
 * already merges manual choices with the scan-computed automatic title matches, so this function
 * only applies it — no matching runs in the client anymore.
 */
export function buildBookChapterRows(options: BuildBookChapterRowsOptions): BookChapterRow[] {
  const readable = [...options.readableChapters].sort(
    (a, b) => a.order - b.order || a.title.localeCompare(b.title) || a.id.localeCompare(b.id),
  );
  const candidates = bookAudioChapterCandidates(options.audioTracks, options.audioChapters);
  const consumedCandidates = new Set<string>();
  const matches = new Map<string, BookAudioChapterCandidate>();

  const readableIds = new Set(readable.map((chapter) => chapter.id));
  const candidateByKey = new Map(candidates.map((candidate) => [candidate.key, candidate]));
  for (const mapping of options.chapterMappings ?? []) {
    if (!readableIds.has(mapping.readableChapterKey) || matches.has(mapping.readableChapterKey)) {
      continue;
    }
    const key = candidateKey(mapping.audioTrackId, mapping.audioMarkerId);
    const candidate = candidateByKey.get(key);
    if (!candidate || consumedCandidates.has(key)) continue;
    consumedCandidates.add(key);
    matches.set(mapping.readableChapterKey, candidate);
  }

  const rows: BookChapterRow[] = readable.map((chapter) => {
    const candidate = matches.get(chapter.id);
    const audioTrack = candidate?.track ?? null;
    return {
      id: `read-${chapter.id}-${chapter.order}`,
      title: chapter.title,
      order: chapter.order,
      depth: chapter.depth,
      readTarget: chapter.target,
      readPageCount: chapter.pageCount ?? null,
      audioTrack,
      audioMarkerId: candidate?.markerId ?? null,
      audioStartSeconds: candidate?.startSeconds ?? null,
      audioEndSeconds: candidate?.endSeconds ?? null,
      isCurrentReading: chapter.id === options.currentReadableId,
      isCurrentAudio: candidate
        ? isCurrentAudioCandidate(
            candidate,
            options.currentAudioTrackId,
            options.currentAudioSeconds,
          )
        : false,
    };
  });

  candidates.forEach((candidate, index) => {
    if (consumedCandidates.has(candidate.key)) return;
    rows.push({
      id: `audio-${candidate.key}`,
      title: candidate.title,
      order: readable.length + index,
      depth: 0,
      readTarget: null,
      readPageCount: null,
      audioTrack: candidate.track,
      audioMarkerId: candidate.markerId,
      audioStartSeconds: candidate.startSeconds,
      audioEndSeconds: candidate.endSeconds,
      isCurrentReading: false,
      isCurrentAudio: isCurrentAudioCandidate(
        candidate,
        options.currentAudioTrackId,
        options.currentAudioSeconds,
      ),
    });
  });

  return rows;
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
