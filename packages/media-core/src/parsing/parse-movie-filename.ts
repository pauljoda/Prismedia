import path from "node:path";
import { parseSeriesFolder } from "./parse-series-folder";
import type { ParsedMovieFilename } from "./types";

const EXTENSION_STRIP = /\.[A-Za-z0-9]{1,5}$/;

export function parseMovieFilename(filePath: string): ParsedMovieFilename {
  if (!filePath) return { title: "", year: null };
  const basename = path.basename(filePath).replace(EXTENSION_STRIP, "");
  const parsed = parseSeriesFolder(basename);
  return { title: parsed.title, year: parsed.year };
}
