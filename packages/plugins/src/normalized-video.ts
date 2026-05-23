/**
 * Normalizer functions for the video subsystem's new typed results:
 * movie, series (with optional cascade children), season, episode.
 * The type declarations live in @prismedia/contracts/normalized-video
 * so the web UI and scrape-accept service can consume them without
 * depending on the plugins engine. This file re-exports those types
 * and provides paired normalizer functions that convert raw plugin
 * output into them, mirroring the style of the existing per-entity
 * normalizers in ./normalizer.ts — trim strings, drop empties,
 * validate URLs, deduplicate case-insensitively, preserve external_ids
 * as-is.
 *
 * These types coexist with the existing NormalizedVideoResult /
 * NormalizedFolderResult / NormalizedGalleryResult / etc. types; they
 * are additive, not replacements, because different plugin workflows
 * still consume different normalized shapes today.
 */

import type {
  ImageCandidate,
  NormalizedCastMember,
  NormalizedMovieResult,
  NormalizedSeriesResult,
  NormalizedSeriesCandidate,
  NormalizedSeasonResult,
  NormalizedEpisodeResult,
} from "@prismedia/contracts";

export type {
  ImageCandidate,
  NormalizedCastMember,
  NormalizedMovieResult,
  NormalizedSeriesResult,
  NormalizedSeriesCandidate,
  NormalizedSeasonResult,
  NormalizedEpisodeResult,
};

// ─── Helpers ────────────────────────────────────────────────────────

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function toStringOrNull(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const trimmed = value.trim();
  return trimmed.length === 0 ? null : trimmed;
}

function toNumberOrNull(value: unknown): number | null {
  if (typeof value === "number" && Number.isFinite(value)) return value;
  if (typeof value === "string") {
    const parsed = Number.parseFloat(value);
    return Number.isFinite(parsed) ? parsed : null;
  }
  return null;
}

function toIntOrNull(value: unknown): number | null {
  const n = toNumberOrNull(value);
  if (n === null) return null;
  return Number.isInteger(n) ? n : Math.trunc(n);
}

function toBooleanOrUndefined(value: unknown): boolean | undefined {
  if (typeof value === "boolean") return value;
  return undefined;
}

function toStringArray(value: unknown): string[] {
  if (!Array.isArray(value)) return [];
  const out: string[] = [];
  const seen = new Set<string>();
  for (const item of value) {
    const normalized = toStringOrNull(item);
    if (!normalized) continue;
    const key = normalized.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    out.push(normalized);
  }
  return out;
}

function toExternalIds(value: unknown): Record<string, string> {
  if (!isObject(value)) return {};
  const out: Record<string, string> = {};
  for (const [key, raw] of Object.entries(value)) {
    const trimmedKey = key.trim();
    const trimmedValue = toStringOrNull(raw);
    if (!trimmedKey || !trimmedValue) continue;
    out[trimmedKey] = trimmedValue;
  }
  return out;
}

function isValidUrl(value: string): boolean {
  return (
    value.startsWith("http://") ||
    value.startsWith("https://") ||
    value.startsWith("data:image/")
  );
}

export function normalizeImageCandidate(value: unknown): ImageCandidate | null {
  if (!isObject(value)) return null;
  const url = toStringOrNull(value.url);
  if (!url || !isValidUrl(url)) return null;
  return {
    url,
    language: toStringOrNull(value.language),
    width: toIntOrNull(value.width) ?? undefined,
    height: toIntOrNull(value.height) ?? undefined,
    aspectRatio: toNumberOrNull(value.aspectRatio) ?? undefined,
    rank: toNumberOrNull(value.rank) ?? undefined,
    source: toStringOrNull(value.source) ?? "unknown",
  };
}

function toImageCandidates(value: unknown): ImageCandidate[] {
  if (!Array.isArray(value)) return [];
  const out: ImageCandidate[] = [];
  for (const raw of value) {
    const candidate = normalizeImageCandidate(raw);
    if (candidate) out.push(candidate);
  }
  return out;
}

function toCastMembers(value: unknown): NormalizedCastMember[] {
  if (!Array.isArray(value)) return [];
  const out: NormalizedCastMember[] = [];
  const seen = new Set<string>();
  for (const raw of value) {
    if (!isObject(raw)) continue;
    const name = toStringOrNull(raw.name);
    if (!name) continue;
    const key = name.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    out.push({
      name,
      character: toStringOrNull(raw.character),
      order: toIntOrNull(raw.order),
      profileUrl: toStringOrNull(raw.profileUrl),
    });
  }
  return out;
}

// ─── Public normalizers ─────────────────────────────────────────────

export function normalizeMovieResult(raw: unknown): NormalizedMovieResult | null {
  if (!isObject(raw)) return null;
  const title = toStringOrNull(raw.title);
  if (!title) return null;
  return {
    title,
    originalTitle: toStringOrNull(raw.originalTitle),
    overview: toStringOrNull(raw.overview),
    tagline: toStringOrNull(raw.tagline),
    releaseDate: toStringOrNull(raw.releaseDate),
    runtime: toIntOrNull(raw.runtime),
    genres: toStringArray(raw.genres),
    studioName: toStringOrNull(raw.studioName),
    cast: toCastMembers(raw.cast),
    posterCandidates: toImageCandidates(raw.posterCandidates),
    backdropCandidates: toImageCandidates(raw.backdropCandidates),
    logoCandidates: toImageCandidates(raw.logoCandidates),
    externalIds: toExternalIds(raw.externalIds),
    rating: toNumberOrNull(raw.rating),
    contentRating: toStringOrNull(raw.contentRating),
  };
}

export function normalizeEpisodeResult(raw: unknown): NormalizedEpisodeResult | null {
  if (!isObject(raw)) return null;
  const seasonNumber = toIntOrNull(raw.seasonNumber) ?? 0;
  const episodeNumber = toIntOrNull(raw.episodeNumber);
  if (episodeNumber === null) return null;
  return {
    seasonNumber,
    episodeNumber,
    absoluteEpisodeNumber: toIntOrNull(raw.absoluteEpisodeNumber),
    title: toStringOrNull(raw.title),
    overview: toStringOrNull(raw.overview),
    airDate: toStringOrNull(raw.airDate),
    runtime: toIntOrNull(raw.runtime),
    stillCandidates: toImageCandidates(raw.stillCandidates),
    guestStars: toCastMembers(raw.guestStars),
    externalIds: toExternalIds(raw.externalIds),
    matched: toBooleanOrUndefined(raw.matched),
    localFilePath: toStringOrNull(raw.localFilePath),
  };
}

export function normalizeSeasonResult(raw: unknown): NormalizedSeasonResult | null {
  if (!isObject(raw)) return null;
  const seasonNumber = toIntOrNull(raw.seasonNumber) ?? 0;
  const episodes: NormalizedEpisodeResult[] = Array.isArray(raw.episodes)
    ? raw.episodes
        .map((e) => normalizeEpisodeResult(e))
        .filter((e): e is NormalizedEpisodeResult => e !== null)
    : [];
  return {
    seasonNumber,
    title: toStringOrNull(raw.title),
    overview: toStringOrNull(raw.overview),
    airDate: toStringOrNull(raw.airDate),
    posterCandidates: toImageCandidates(raw.posterCandidates),
    externalIds: toExternalIds(raw.externalIds),
    episodes,
  };
}

export function normalizeSeriesResult(raw: unknown): NormalizedSeriesResult | null {
  if (!isObject(raw)) return null;
  const title = toStringOrNull(raw.title);
  if (!title) return null;

  const seasons: NormalizedSeasonResult[] = Array.isArray(raw.seasons)
    ? raw.seasons
        .map((s) => normalizeSeasonResult(s))
        .filter((s): s is NormalizedSeasonResult => s !== null)
    : [];

  const candidates: NormalizedSeriesCandidate[] | undefined = Array.isArray(
    raw.candidates,
  )
    ? raw.candidates
        .map((c): NormalizedSeriesCandidate | null => {
          if (!isObject(c)) return null;
          const candTitle = toStringOrNull(c.title);
          if (!candTitle) return null;
          return {
            externalIds: toExternalIds(c.externalIds),
            title: candTitle,
            year: toIntOrNull(c.year),
            overview: toStringOrNull(c.overview),
            posterUrl: toStringOrNull(c.posterUrl),
            popularity: toNumberOrNull(c.popularity),
          };
        })
        .filter((c): c is NormalizedSeriesCandidate => c !== null)
    : undefined;

  const status = toStringOrNull(raw.status);
  const validStatus =
    status === "returning" || status === "ended" ||
    status === "canceled" || status === "unknown"
      ? status
      : null;

  return {
    title,
    originalTitle: toStringOrNull(raw.originalTitle),
    overview: toStringOrNull(raw.overview),
    tagline: toStringOrNull(raw.tagline),
    firstAirDate: toStringOrNull(raw.firstAirDate),
    endAirDate: toStringOrNull(raw.endAirDate),
    status: validStatus,
    genres: toStringArray(raw.genres),
    studioName: toStringOrNull(raw.studioName),
    cast: toCastMembers(raw.cast),
    posterCandidates: toImageCandidates(raw.posterCandidates),
    backdropCandidates: toImageCandidates(raw.backdropCandidates),
    logoCandidates: toImageCandidates(raw.logoCandidates),
    externalIds: toExternalIds(raw.externalIds),
    seasons,
    candidates,
  };
}
