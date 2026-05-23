/**
 * Video classification result. Given a file's absolute path and its
 * library root configuration, the classifier decides which typed
 * entity the file should become: a movie, an episode, or neither.
 *
 * Classification is pure. It never touches the filesystem or the
 * database — it only looks at the path segments and the toggles.
 */
export type VideoClassification =
  | VideoClassificationMovie
  | VideoClassificationEpisode
  | VideoClassificationSkipped
  | VideoClassificationRejected;

export interface VideoClassificationMovie {
  kind: "movie";
  filePath: string;
  libraryRootPath: string;
}

export interface VideoClassificationEpisode {
  kind: "episode";
  filePath: string;
  libraryRootPath: string;
  /** Absolute path of the series folder (depth-1 under the library root). */
  seriesFolderPath: string;
  /** Series folder basename used as the default display title. */
  seriesFolderName: string;
  /**
   * Absolute path of the season folder (depth-2 under the library root),
   * or `null` for a flat series where the file lives directly under the
   * series folder (Case A in the spec).
   */
  seasonFolderPath: string | null;
  /** Season folder basename. Null if no season folder exists. */
  seasonFolderName: string | null;
  /**
   * Season number placement per the Case A / Case B rules:
   *
   * - Case A (no season folders under the series) → 0 (the flat series
   *   lives in a single synthetic season).
   * - Case B with a recognized season folder → parsed season number.
   * - Case B, loose file at the series root → 0 (Specials).
   * - Case B, unrecognized folder → 0 as a conservative default.
   */
  placementSeasonNumber: number;
}

export interface VideoClassificationSkipped {
  kind: "skipped";
  filePath: string;
  reason: string;
}

export interface VideoClassificationRejected {
  kind: "rejected";
  filePath: string;
  reason: string;
}

export interface LibraryClassificationConfig {
  libraryRootPath: string;
}
