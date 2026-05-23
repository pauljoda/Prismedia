import type { ParsedSeriesFolder } from "./types";

const YEAR_PATTERN_PAREN = /\((\d{4})\)/;
const YEAR_PATTERN_BRACKET = /\[(\d{4})\]/;
const YEAR_PATTERN_BARE = /(?:^|[\s.])((?:19|20)\d{2})(?:[\s.]|$)/;

// Tokens we strip after extracting year/title. Match common release-group
// artifacts — resolution, HDR, codec, source, group suffix.
const RELEASE_TOKEN_PATTERNS: RegExp[] = [
  /\b\d{3,4}p\b/gi,              // 480p, 720p, 1080p, 2160p
  /\bhdr\d*\b/gi,                // HDR, HDR10
  /\bsdr\b/gi,
  /\bweb[-.]?dl\b/gi,
  /\bweb[-.]?rip\b/gi,
  /\bbluray\b/gi,
  /\bbdrip\b/gi,
  /\bdvdrip\b/gi,
  /\bhdtv\b/gi,
  /\bx26[45]\b/gi,
  /\bh[-.]?26[45]\b/gi,
  /\bhevc\b/gi,
  /\bavc\b/gi,
  /\baac\b/gi,
  /\bac3\b/gi,
  /\bdts\b/gi,
  /-[A-Za-z0-9]+$/,              // trailing "-GROUP"
];

export function parseSeriesFolder(name: string): ParsedSeriesFolder {
  if (!name) return { title: "", year: null };

  let working = name;

  let year: number | null = null;
  const parenMatch = working.match(YEAR_PATTERN_PAREN);
  if (parenMatch) {
    year = Number.parseInt(parenMatch[1], 10);
    working = working.replace(YEAR_PATTERN_PAREN, " ");
  } else {
    const bracketMatch = working.match(YEAR_PATTERN_BRACKET);
    if (bracketMatch) {
      year = Number.parseInt(bracketMatch[1], 10);
      working = working.replace(YEAR_PATTERN_BRACKET, " ");
    } else {
      const bareMatch = working.match(YEAR_PATTERN_BARE);
      if (bareMatch) {
        year = Number.parseInt(bareMatch[1], 10);
        working = working.replace(bareMatch[1], " ");
      }
    }
  }

  for (const pattern of RELEASE_TOKEN_PATTERNS) {
    working = working.replace(pattern, " ");
  }

  const title = working
    .replace(/[._]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();

  return { title, year };
}
