import type { ParsedSeasonFolder } from "./types";

const SPECIALS_WORDS = new Set([
  "specials",
  "special",
  "extras",
  "ova",
]);

const SEASON_LONG_PATTERNS: RegExp[] = [
  /^\s*season[\s._-]*(\d{1,3})\s*$/i,          // Season 1, Season_01, season.1
  /^\s*saison[\s._-]*(\d{1,3})\s*$/i,          // French
  /^\s*temporada[\s._-]*(\d{1,3})\s*$/i,       // Spanish / Portuguese
  /^\s*staffel[\s._-]*(\d{1,3})\s*$/i,         // German
];

const SEASON_SHORT_PATTERN = /^\s*s[\s._-]*(\d{1,3})\s*$/i;   // S1, S01, S 01
const BARE_NUMBER_PATTERN = /^\s*(\d{1,3})\s*$/;              // 1, 01, 12

export function parseSeasonFolder(name: string): ParsedSeasonFolder {
  if (!name) return { seasonNumber: null, title: null };

  const trimmed = name.trim();
  const lower = trimmed.toLowerCase();

  if (SPECIALS_WORDS.has(lower)) {
    return { seasonNumber: 0, title: "Specials" };
  }

  for (const pattern of SEASON_LONG_PATTERNS) {
    const match = trimmed.match(pattern);
    if (match) {
      return { seasonNumber: Number.parseInt(match[1], 10), title: null };
    }
  }

  const shortMatch = trimmed.match(SEASON_SHORT_PATTERN);
  if (shortMatch) {
    return { seasonNumber: Number.parseInt(shortMatch[1], 10), title: null };
  }

  const bareMatch = trimmed.match(BARE_NUMBER_PATTERN);
  if (bareMatch) {
    return { seasonNumber: Number.parseInt(bareMatch[1], 10), title: null };
  }

  return { seasonNumber: null, title: null };
}
