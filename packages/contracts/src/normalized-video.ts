/**
 * Normalized result type declarations for the video subsystem's new
 * typed results: movie, series (with optional cascade children), season,
 * episode. Lives in @prismedia/contracts so both the plugins engine and
 * the web UI / scrape-accept service can import without creating a
 * circular dependency. The matching normalizer functions live in
 * @prismedia/plugins (packages/plugins/src/normalized-video.ts).
 */

export interface ImageCandidate {
  url: string;
  language?: string | null;
  width?: number;
  height?: number;
  aspectRatio?: number;
  rank?: number;
  source: string;
}

export interface NormalizedCastMember {
  name: string;
  character?: string | null;
  order?: number | null;
  /** TMDB-hosted profile image URL (w185 size). Downloaded to local
   *  disk during cascade accept for offline availability. */
  profileUrl?: string | null;
}

export interface NormalizedMovieResult {
  title: string;
  originalTitle?: string | null;
  overview?: string | null;
  tagline?: string | null;
  releaseDate?: string | null;
  runtime?: number | null;
  genres: string[];
  studioName?: string | null;
  cast?: NormalizedCastMember[];
  posterCandidates: ImageCandidate[];
  backdropCandidates: ImageCandidate[];
  logoCandidates: ImageCandidate[];
  externalIds: Record<string, string>;
  rating?: number | null;
  contentRating?: string | null;
}

export interface NormalizedSeriesResult {
  title: string;
  originalTitle?: string | null;
  overview?: string | null;
  tagline?: string | null;
  firstAirDate?: string | null;
  endAirDate?: string | null;
  status?: "returning" | "ended" | "canceled" | "unknown" | null;
  genres: string[];
  studioName?: string | null;
  cast?: NormalizedCastMember[];
  posterCandidates: ImageCandidate[];
  backdropCandidates: ImageCandidate[];
  logoCandidates: ImageCandidate[];
  externalIds: Record<string, string>;
  seasons: NormalizedSeasonResult[];
  candidates?: NormalizedSeriesCandidate[];
}

export interface NormalizedSeriesCandidate {
  externalIds: Record<string, string>;
  title: string;
  year?: number | null;
  overview?: string | null;
  posterUrl?: string | null;
  popularity?: number | null;
}

export interface NormalizedSeasonResult {
  seasonNumber: number;
  title?: string | null;
  overview?: string | null;
  airDate?: string | null;
  posterCandidates: ImageCandidate[];
  externalIds: Record<string, string>;
  episodes: NormalizedEpisodeResult[];
}

export interface NormalizedEpisodeResult {
  seasonNumber: number;
  episodeNumber: number;
  absoluteEpisodeNumber?: number | null;
  title?: string | null;
  overview?: string | null;
  airDate?: string | null;
  runtime?: number | null;
  stillCandidates: ImageCandidate[];
  guestStars?: NormalizedCastMember[];
  externalIds: Record<string, string>;
  matched?: boolean;
  localFilePath?: string | null;
}
