import path from "node:path";
import type { ParsedEpisodeFilename } from "./types";

const EXTENSION_STRIP = /\.[A-Za-z0-9]{1,5}$/;

const EPISODE_PATTERNS: RegExp[] = [
  /s(\d{1,3})[\s._-]*x?e(\d{1,3})/i,
  /(?:^|[^\d])(\d{1,2})x(\d{1,3})(?!\d)/,
  /season[\s._-]*(\d{1,3})[\s._-]+episode[\s._-]*(\d{1,3})/i,
];

const ABSOLUTE_EPISODE_PATTERN = /(?:^|[\s._-])-?[\s._-]*(\d{2,4})[\s._-]+/;

const YEAR_PATTERN_PAREN = /\((\d{4})\)/;
const YEAR_PATTERN_BARE = /(?:^|[\s._-])((?:19|20)\d{2})(?:[\s._-]|$)/;

const RESOLUTION_TOKEN = /\b\d{3,4}p\b/i;

function stripExtension(name: string): string {
  return name.replace(EXTENSION_STRIP, "");
}

function extractYear(input: string): { year: number | null; cleaned: string } {
  const parenMatch = input.match(YEAR_PATTERN_PAREN);
  if (parenMatch) {
    return {
      year: Number.parseInt(parenMatch[1], 10),
      cleaned: input.replace(YEAR_PATTERN_PAREN, " "),
    };
  }
  const bareMatch = input.match(YEAR_PATTERN_BARE);
  if (bareMatch) {
    return {
      year: Number.parseInt(bareMatch[1], 10),
      cleaned: input.replace(bareMatch[1], " "),
    };
  }
  return { year: null, cleaned: input };
}

function cleanTitle(raw: string): string {
  return raw
    .replace(RESOLUTION_TOKEN, " ")
    .replace(/[._]+/g, " ")
    .replace(/\s+/g, " ")
    .replace(/^[\s\-_.]+|[\s\-_.]+$/g, "")
    .trim();
}

export function parseEpisodeFilename(filePath: string): ParsedEpisodeFilename {
  const basename = path.basename(filePath);
  const withoutExt = stripExtension(basename);
  const yearResult = extractYear(withoutExt);
  const working = yearResult.cleaned;
  const year = yearResult.year;

  for (const pattern of EPISODE_PATTERNS) {
    const match = working.match(pattern);
    if (match) {
      const seasonNumber = Number.parseInt(match[1], 10);
      const episodeNumber = Number.parseInt(match[2], 10);
      const afterMatch = working.slice((match.index ?? 0) + match[0].length);
      const title = cleanTitle(afterMatch) || null;
      return {
        seasonNumber,
        episodeNumber,
        absoluteEpisodeNumber: null,
        title,
        year,
      };
    }
  }

  const absScratch = working.replace(RESOLUTION_TOKEN, " ");
  const absMatch = absScratch.match(ABSOLUTE_EPISODE_PATTERN);
  if (absMatch) {
    const candidate = Number.parseInt(absMatch[1], 10);
    if (candidate < 1900 || candidate > 2099) {
      const afterMatch = absScratch.slice((absMatch.index ?? 0) + absMatch[0].length);
      const title = cleanTitle(afterMatch) || null;
      return {
        seasonNumber: null,
        episodeNumber: null,
        absoluteEpisodeNumber: candidate,
        title,
        year,
      };
    }
  }

  return {
    seasonNumber: null,
    episodeNumber: null,
    absoluteEpisodeNumber: null,
    title: null,
    year,
  };
}
