/**
 * Normalizers for all entity result types.
 *
 * Each normalizer takes a raw result object (from a plugin's stdout or TS return)
 * and produces a validated, trimmed, deduplicated result.
 */

import type { ImageCandidate } from "@prismedia/contracts";

import type {
  NormalizedVideoResult,
  NormalizedFolderResult,
  NormalizedGalleryResult,
  NormalizedGalleryCandidate,
  NormalizedBookResult,
  NormalizedBookCandidate,
  NormalizedBookVolumeCover,
  NormalizedImageResult,
  NormalizedAudioTrackResult,
  NormalizedAudioLibraryResult,
  NormalizedSeriesRef,
  EpisodeMapping,
} from "./types";

// ─── Utility helpers ───────────────────────────────────────────────

function trimOrNull(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const trimmed = value.trim();
  return trimmed || null;
}

function toUrlArray(value: unknown): string[] {
  if (Array.isArray(value)) {
    return value
      .filter((v): v is string => typeof v === "string")
      .map((v) => v.trim())
      .filter(
        (v) =>
          v.startsWith("http://") ||
          v.startsWith("https://") ||
          v.startsWith("data:"),
      );
  }
  if (typeof value === "string") {
    const trimmed = value.trim();
    if (
      trimmed.startsWith("http://") ||
      trimmed.startsWith("https://") ||
      trimmed.startsWith("data:")
    ) {
      return [trimmed];
    }
  }
  return [];
}

function toStringArray(value: unknown): string[] {
  if (!Array.isArray(value)) return [];
  const seen = new Set<string>();
  const result: string[] = [];
  for (const item of value) {
    if (typeof item !== "string") continue;
    const trimmed = item.trim();
    if (!trimmed) continue;
    const lower = trimmed.toLowerCase();
    if (seen.has(lower)) continue;
    seen.add(lower);
    result.push(trimmed);
  }
  return result;
}

function toNumber(value: unknown): number | null {
  if (typeof value === "number" && Number.isFinite(value)) return value;
  if (typeof value === "string") {
    const parsed = parseInt(value, 10);
    if (Number.isFinite(parsed)) return parsed;
  }
  return null;
}

function toFiniteNumber(value: unknown): number | null {
  if (typeof value === "number" && Number.isFinite(value)) return value;
  if (typeof value === "string") {
    const parsed = Number.parseFloat(value);
    if (Number.isFinite(parsed)) return parsed;
  }
  return null;
}

function toBoolean(value: unknown): boolean | undefined {
  return typeof value === "boolean" ? value : undefined;
}

function trimToUrl(value: unknown): string | null {
  const trimmed = trimOrNull(value);
  if (!trimmed) return null;
  if (
    trimmed.startsWith("http://") ||
    trimmed.startsWith("https://") ||
    trimmed.startsWith("data:image/")
  ) {
    return trimmed;
  }
  return null;
}

function toExternalIds(value: unknown): Record<string, string> | undefined {
  if (!value || typeof value !== "object" || Array.isArray(value)) return undefined;
  const out: Record<string, string> = {};
  for (const [key, raw] of Object.entries(value as Record<string, unknown>)) {
    const trimmedKey = key.trim();
    const trimmedValue = trimOrNull(raw);
    if (!trimmedKey || !trimmedValue) continue;
    out[trimmedKey] = trimmedValue;
  }
  return Object.keys(out).length > 0 ? out : undefined;
}

function toCandidates<T extends NormalizedGalleryCandidate | NormalizedBookCandidate>(
  value: unknown,
): T[] | undefined {
  if (!Array.isArray(value)) return undefined;
  const candidates: T[] = [];
  for (const raw of value) {
    if (!raw || typeof raw !== "object" || Array.isArray(raw)) continue;
    const item = raw as Record<string, unknown>;
    const title = trimOrNull(item.title);
    if (!title) continue;
    candidates.push({
      externalIds: toExternalIds(item.externalIds) ?? {},
      title,
      year: toNumber(item.year) ?? undefined,
      overview: trimOrNull(item.overview),
      posterUrl: trimToUrl(item.posterUrl),
      language: trimOrNull(item.language),
      contentRating: trimOrNull(item.contentRating),
      source: trimOrNull(item.source),
      popularity: toFiniteNumber(item.popularity) ?? undefined,
    } as T);
  }
  return candidates.length > 0 ? candidates : undefined;
}

function toImageCandidates(value: unknown): ImageCandidate[] | undefined {
  if (!Array.isArray(value)) return undefined;
  const candidates: ImageCandidate[] = [];
  for (const raw of value) {
    if (!raw || typeof raw !== "object" || Array.isArray(raw)) continue;
    const item = raw as Record<string, unknown>;
    const url = trimToUrl(item.url);
    if (!url) continue;
    candidates.push({
      url,
      language: trimOrNull(item.language),
      width: toNumber(item.width) ?? undefined,
      height: toNumber(item.height) ?? undefined,
      aspectRatio: toFiniteNumber(item.aspectRatio) ?? undefined,
      rank: toFiniteNumber(item.rank) ?? undefined,
      source: trimOrNull(item.source) ?? "plugin",
    });
  }
  return candidates.length > 0 ? candidates : undefined;
}

function toImageCandidateMap(value: unknown): Record<string, ImageCandidate> | undefined {
  if (!value || typeof value !== "object" || Array.isArray(value)) return undefined;
  const out: Record<string, ImageCandidate> = {};
  for (const [key, raw] of Object.entries(value as Record<string, unknown>)) {
    const candidate = toImageCandidates([raw])?.[0];
    const normalizedKey = key.trim();
    if (!normalizedKey || !candidate) continue;
    out[normalizedKey] = candidate;
  }
  return Object.keys(out).length > 0 ? out : undefined;
}

function toStringRecord(value: unknown): Record<string, string> | undefined {
  if (!value || typeof value !== "object" || Array.isArray(value)) return undefined;
  const out: Record<string, string> = {};
  for (const [key, raw] of Object.entries(value as Record<string, unknown>)) {
    const normalizedKey = key.trim();
    const normalizedValue = trimOrNull(raw);
    if (!normalizedKey || !normalizedValue) continue;
    out[normalizedKey] = normalizedValue;
  }
  return Object.keys(out).length > 0 ? out : undefined;
}

function toVolumeCovers(value: unknown): NormalizedBookVolumeCover[] | undefined {
  if (!Array.isArray(value)) return undefined;
  const out: NormalizedBookVolumeCover[] = [];
  for (const raw of value) {
    if (!raw || typeof raw !== "object" || Array.isArray(raw)) continue;
    const item = raw as Record<string, unknown>;
    const candidate = toImageCandidates([item])?.[0];
    const volumeNumber = trimOrNull(item.volumeNumber ?? item.volume);
    if (!candidate || !volumeNumber) continue;
    out.push({
      ...candidate,
      volumeNumber,
      title: trimOrNull(item.title),
      externalIds: toExternalIds(item.externalIds),
    });
  }
  return out.length > 0 ? out : undefined;
}

// ─── Video Result Normalizer ───────────────────────────────────────

export function normalizeVideoResult(
  raw: Record<string, unknown>,
): NormalizedVideoResult {
  let series: NormalizedSeriesRef | null = null;
  if (raw.series && typeof raw.series === "object") {
    const s = raw.series as Record<string, unknown>;
    const name = trimOrNull(s.name);
    if (name) {
      series = {
        name,
        externalId: trimOrNull(s.externalId) ?? undefined,
        season: toNumber(s.season) ?? undefined,
        episode: toNumber(s.episode) ?? undefined,
      };
    }
  }

  return {
    title: trimOrNull(raw.title),
    date: trimOrNull(raw.date),
    details: trimOrNull(raw.details),
    urls: toUrlArray(raw.urls ?? raw.url),
    studioName: trimOrNull(raw.studioName ?? raw.studio),
    performerNames: toStringArray(raw.performerNames ?? raw.performers),
    tagNames: toStringArray(raw.tagNames ?? raw.tags),
    imageUrl: trimToUrl(raw.imageUrl ?? raw.image),
    episodeNumber: toNumber(raw.episodeNumber ?? raw.episode_number),
    series,
    code: trimOrNull(raw.code),
    director: trimOrNull(raw.director),
  };
}

export function hasUsableVideoResult(result: NormalizedVideoResult): boolean {
  return Boolean(
    result.title ||
      result.date ||
      result.details ||
      result.urls.length > 0 ||
      result.studioName ||
      result.imageUrl ||
      result.performerNames.length > 0 ||
      result.tagNames.length > 0 ||
      result.episodeNumber != null,
  );
}

// ─── Folder Result Normalizer ──────────────────────────────────────

export function normalizeFolderResult(
  raw: Record<string, unknown>,
): NormalizedFolderResult {
  let episodeMap: Record<string, EpisodeMapping> | undefined;
  if (raw.episodeMap && typeof raw.episodeMap === "object") {
    episodeMap = {};
    for (const [key, val] of Object.entries(
      raw.episodeMap as Record<string, unknown>,
    )) {
      if (!val || typeof val !== "object") continue;
      const ep = val as Record<string, unknown>;
      episodeMap[key] = {
        episodeNumber: toNumber(ep.episodeNumber) ?? 0,
        seasonNumber: toNumber(ep.seasonNumber) ?? 0,
        title: trimOrNull(ep.title),
        date: trimOrNull(ep.date),
        details: trimOrNull(ep.details),
      };
    }
  }

  return {
    name: trimOrNull(raw.name ?? raw.title),
    details: trimOrNull(raw.details),
    date: trimOrNull(raw.date),
    imageUrl: trimToUrl(raw.imageUrl ?? raw.image),
    backdropUrl: trimToUrl(raw.backdropUrl ?? raw.backdrop),
    studioName: trimOrNull(raw.studioName ?? raw.studio),
    tagNames: toStringArray(raw.tagNames ?? raw.tags),
    urls: toUrlArray(raw.urls ?? raw.url),
    seriesExternalId: trimOrNull(raw.seriesExternalId) ?? undefined,
    seasonNumber: toNumber(raw.seasonNumber) ?? undefined,
    totalEpisodes: toNumber(raw.totalEpisodes) ?? undefined,
    episodeMap,
  };
}

// ─── Gallery Result Normalizer ─────────────────────────────────────

export function normalizeGalleryResult(
  raw: Record<string, unknown>,
): NormalizedGalleryResult {
  return {
    title: trimOrNull(raw.title),
    date: trimOrNull(raw.date),
    details: trimOrNull(raw.details),
    urls: toUrlArray(raw.urls ?? raw.url),
    studioName: trimOrNull(raw.studioName ?? raw.studio),
    performerNames: toStringArray(raw.performerNames ?? raw.performers),
    tagNames: toStringArray(raw.tagNames ?? raw.tags),
    imageUrl: trimToUrl(raw.imageUrl ?? raw.image),
    photographer: trimOrNull(raw.photographer),
    externalIds: toExternalIds(raw.externalIds),
    candidates: toCandidates<NormalizedGalleryCandidate>(raw.candidates),
    isNsfw: toBoolean(raw.isNsfw),
  };
}

// ─── Book Result Normalizer ────────────────────────────────────────

export function normalizeBookResult(
  raw: Record<string, unknown>,
): NormalizedBookResult {
  return {
    title: trimOrNull(raw.title ?? raw.name),
    date: trimOrNull(raw.date),
    details: trimOrNull(raw.details),
    urls: toUrlArray(raw.urls ?? raw.url),
    studioName: trimOrNull(raw.studioName ?? raw.studio),
    performerNames: toStringArray(raw.performerNames ?? raw.performers),
    tagNames: toStringArray(raw.tagNames ?? raw.tags),
    imageUrl: trimToUrl(raw.imageUrl ?? raw.image),
    chapterImageUrl: trimToUrl(raw.chapterImageUrl ?? raw.chapterImage),
    chapterNumber: toNumber(raw.chapterNumber ?? raw.chapter_number),
    imageCandidates: toImageCandidates(raw.imageCandidates),
    chapterImageCandidates: toImageCandidates(raw.chapterImageCandidates),
    chapterImageByNumber: toImageCandidateMap(raw.chapterImageByNumber),
    volumeCovers: toVolumeCovers(raw.volumeCovers),
    chapterVolumeByNumber: toStringRecord(raw.chapterVolumeByNumber),
    chapterTitleByNumber: toStringRecord(raw.chapterTitleByNumber),
    externalIds: toExternalIds(raw.externalIds),
    candidates: toCandidates<NormalizedBookCandidate>(raw.candidates),
    isNsfw: toBoolean(raw.isNsfw),
  };
}

// ─── Image Result Normalizer ───────────────────────────────────────

export function normalizeImageResult(
  raw: Record<string, unknown>,
): NormalizedImageResult {
  return {
    title: trimOrNull(raw.title),
    date: trimOrNull(raw.date),
    details: trimOrNull(raw.details),
    urls: toUrlArray(raw.urls ?? raw.url),
    tagNames: toStringArray(raw.tagNames ?? raw.tags),
    imageUrl: trimToUrl(raw.imageUrl ?? raw.image),
  };
}

// ─── Audio Track Result Normalizer ─────────────────────────────────

export function normalizeAudioTrackResult(
  raw: Record<string, unknown>,
): NormalizedAudioTrackResult {
  return {
    title: trimOrNull(raw.title),
    artist: trimOrNull(raw.artist),
    album: trimOrNull(raw.album),
    trackNumber: toNumber(raw.trackNumber ?? raw.track_number),
    date: trimOrNull(raw.date),
    details: trimOrNull(raw.details),
    imageUrl: trimToUrl(raw.imageUrl ?? raw.image),
    urls: toUrlArray(raw.urls ?? raw.url),
    tagNames: toStringArray(raw.tagNames ?? raw.tags),
  };
}

// ─── Audio Library Result Normalizer ───────────────────────────────

export function normalizeAudioLibraryResult(
  raw: Record<string, unknown>,
): NormalizedAudioLibraryResult {
  return {
    name: trimOrNull(raw.name ?? raw.title),
    artist: trimOrNull(raw.artist),
    details: trimOrNull(raw.details),
    date: trimOrNull(raw.date),
    imageUrl: trimToUrl(raw.imageUrl ?? raw.image),
    urls: toUrlArray(raw.urls ?? raw.url),
    tagNames: toStringArray(raw.tagNames ?? raw.tags),
    trackCount: toNumber(raw.trackCount) ?? undefined,
  };
}
