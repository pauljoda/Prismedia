import type { StashScrapedScene, StashScrapedPerformer, NormalizedScrapeResult } from "./types";
import type { NormalizedPerformerResult } from "@prismedia/contracts";

/**
 * Normalize a raw Stash scraper scene result into Prismedia domain types.
 * Trims whitespace, deduplicates names, normalizes dates.
 */
export function normalizeSceneResult(
  raw: StashScrapedScene
): NormalizedScrapeResult {
  const performerNames = deduplicateNames(
    (raw.performers ?? []).map((p) => p.name).filter(Boolean)
  );

  const tagNames = deduplicateNames(
    (raw.tags ?? []).map((t) => t.name).filter(Boolean)
  );

  const url = raw.url ?? raw.urls?.[0] ?? null;

  return {
    title: trimOrNull(raw.title),
    date: normalizeDate(raw.date),
    details: trimOrNull(raw.details),
    url: trimOrNull(url),
    studioName: trimOrNull(raw.studio?.name),
    performerNames,
    tagNames,
    imageUrl: trimToUrl(raw.image),
  };
}

export function hasUsableNormalizedSceneResult(
  result: NormalizedScrapeResult
): boolean {
  return Boolean(
    result.title ||
      result.date ||
      result.details ||
      result.url ||
      result.studioName ||
      result.imageUrl ||
      result.performerNames.length > 0 ||
      result.tagNames.length > 0
  );
}

/** Only return the value if it looks like a valid URL, otherwise null */
function trimToUrl(value: string | undefined | null): string | null {
  const trimmed = trimOrNull(value);
  if (!trimmed) return null;
  if (trimmed.startsWith("http://") || trimmed.startsWith("https://") || trimmed.startsWith("data:image/")) {
    return trimmed;
  }
  return null;
}

/** Common HTML entities that scrapers return in metadata fields. */
const htmlEntities: Record<string, string> = {
  "&amp;": "&", "&lt;": "<", "&gt;": ">", "&quot;": '"',
  "&#39;": "'", "&apos;": "'", "&#x27;": "'", "&#x2F;": "/",
  "&nbsp;": " ",
};
const htmlEntityPattern = new RegExp(Object.keys(htmlEntities).join("|"), "gi");

function decodeHtmlEntities(value: string): string {
  return value.replace(htmlEntityPattern, (match) => htmlEntities[match.toLowerCase()] ?? match);
}

function trimOrNull(value: string | undefined | null): string | null {
  if (!value) return null;
  const trimmed = decodeHtmlEntities(value.trim());
  return trimmed || null;
}

function deduplicateUrls(urls: string[]): string[] {
  const seen = new Set<string>();
  const result: string[] = [];
  for (const url of urls) {
    // For data URLs, use first 100 chars as key to avoid huge string comparisons
    const key = url.startsWith("data:") ? url.slice(0, 100) : url;
    if (seen.has(key)) continue;
    seen.add(key);
    result.push(url);
  }
  return result;
}

function deduplicateNames(names: string[]): string[] {
  const seen = new Set<string>();
  const result: string[] = [];

  for (const name of names) {
    const trimmed = name.trim();
    if (!trimmed) continue;
    const lower = trimmed.toLowerCase();
    if (seen.has(lower)) continue;
    seen.add(lower);
    result.push(trimmed);
  }

  return result;
}

/**
 * Normalize date strings to YYYY-MM-DD format.
 * Handles common formats: YYYY-MM-DD, MM/DD/YYYY, DD/MM/YYYY, etc.
 */
function normalizeDate(value: string | undefined | null): string | null {
  const trimmed = trimOrNull(value);
  if (!trimmed) return null;

  // Preserve partial dates rather than coercing them to the first of the month/year.
  if (/^\d{4}$/.test(trimmed) || /^\d{4}-\d{2}$/.test(trimmed)) {
    return trimmed;
  }

  // Already YYYY-MM-DD
  if (/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) {
    return trimmed;
  }

  // Structured payloads from misconfigured selectors should not leak into the date field.
  if (
    trimmed.startsWith("{") ||
    trimmed.startsWith("[") ||
    trimmed.startsWith("<") ||
    trimmed.startsWith("http://") ||
    trimmed.startsWith("https://")
  ) {
    return null;
  }

  // Try parsing as a date
  const parsed = new Date(trimmed);
  if (!Number.isNaN(parsed.getTime())) {
    const year = parsed.getFullYear();
    const month = String(parsed.getMonth() + 1).padStart(2, "0");
    const day = String(parsed.getDate()).padStart(2, "0");
    return `${year}-${month}-${day}`;
  }

  return null;
}

/**
 * Normalize a raw Stash scraper performer result into Prismedia domain types.
 */
export function normalizePerformerResult(
  raw: StashScrapedPerformer
): NormalizedPerformerResult {
  return {
    name: trimOrNull(raw.name),
    disambiguation: trimOrNull(raw.disambiguation),
    gender: trimOrNull(raw.gender),
    birthdate: normalizeDate(raw.birthdate),
    country: trimOrNull(raw.country),
    ethnicity: trimOrNull(raw.ethnicity),
    eyeColor: trimOrNull(raw.eye_color),
    hairColor: trimOrNull(raw.hair_color),
    height: trimOrNull(raw.height),
    weight: trimOrNull(raw.weight),
    measurements: trimOrNull(raw.measurements),
    tattoos: trimOrNull(raw.tattoos),
    piercings: trimOrNull(raw.piercings),
    aliases: trimOrNull(raw.aliases),
    details: trimOrNull(raw.details),
    imageUrl: trimToUrl(raw.image) ?? trimToUrl(raw.images?.[0]),
    imageUrls: deduplicateUrls([
      ...(raw.image ? [trimToUrl(raw.image)] : []),
      ...(raw.images ?? []).map((img) => trimToUrl(img)),
    ].filter((u): u is string => u !== null)),
    tagNames: deduplicateNames(
      (raw.tags ?? []).map((t) => t.name).filter(Boolean)
    ),
  };
}
