/**
 * User-facing names for library entities. Single source of truth for
 * UI copy so the same labels can be shifted centrally if we ever
 * rename again.
 */
export const entityTerms = {
  videos: "Videos",
  video: "Video",
  series: "Series",
  seriesSingular: "Series",
  seasons: "Seasons",
  season: "Season",
  movies: "Movies",
  movie: "Movie",
  performers: "People",
  performer: "Person",
  studios: "Studios",
  studio: "Studio",
  tags: "Tags",
  tag: "Tag",
} as const;

export type EntityTerms = typeof entityTerms;

export function formatVideoCount(count: number): string {
  const w =
    count === 1
      ? entityTerms.video.toLowerCase()
      : entityTerms.videos.toLowerCase();
  return `${count} ${w}`;
}

export function useTerms(): EntityTerms {
  return entityTerms;
}
