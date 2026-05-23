export interface ParsedSeasonFolder {
  /** Parsed season number (0 = Specials). Null when unrecognized. */
  seasonNumber: number | null;
  /** Optional display title if the folder name contained one. */
  title: string | null;
}

export interface ParsedSeriesFolder {
  /** Cleaned-up display title derived from the folder name. */
  title: string;
  /** Year parsed from a trailing `(YYYY)` or `[YYYY]` hint. */
  year: number | null;
}

export interface ParsedEpisodeFilename {
  seasonNumber: number | null;
  episodeNumber: number | null;
  absoluteEpisodeNumber: number | null;
  title: string | null;
  year: number | null;
}

export interface ParsedMovieFilename {
  title: string;
  year: number | null;
}
