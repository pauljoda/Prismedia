/**
 * Normalize StashBox GraphQL responses into Prismedia domain types.
 *
 * This is the unification point — StashBox results become the same shape
 * that community scraper results use, so the accept flow works for both.
 */

import type { NormalizedScrapeResult } from "../types";
import type { NormalizedPerformerResult } from "@prismedia/contracts";
import type { StashBoxScene, StashBoxPerformer } from "./types";

/**
 * Convert a StashBox scene into a NormalizedScrapeResult.
 * Also stores the full raw scene as the rawResult for enrichment
 * (studio URL/image, performer metadata, etc.)
 */
export function normalizeStashBoxScene(
  scene: StashBoxScene,
): NormalizedScrapeResult {
  const performerNames = deduplicateNames(
    scene.performers?.map((pa) => pa.performer.name).filter(Boolean) ?? [],
  );

  const tagNames = deduplicateNames(
    scene.tags?.map((t) => t.name).filter(Boolean) ?? [],
  );

  const url = scene.urls?.[0]?.url ?? null;
  const imageUrl = scene.images?.[0]?.url ?? null;

  return {
    title: scene.title ?? null,
    date: scene.date ?? null,
    details: scene.details ?? null,
    url,
    studioName: scene.studio?.name ?? null,
    performerNames,
    tagNames,
    imageUrl,
  };
}

/**
 * Convert a StashBox performer into a NormalizedPerformerResult.
 */
export function normalizeStashBoxPerformer(
  performer: StashBoxPerformer,
): NormalizedPerformerResult {
  const tattoos = performer.tattoos
    ?.map((t) => [t.location, t.description].filter(Boolean).join(": "))
    .join("; ") || null;

  const piercings = performer.piercings
    ?.map((p) => [p.location, p.description].filter(Boolean).join(": "))
    .join("; ") || null;

  const measurements = formatMeasurements(performer.measurements);

  const imageUrls = (performer.images ?? [])
    .map((img) => img.url)
    .filter(Boolean);

  return {
    name: performer.name ?? null,
    disambiguation: performer.disambiguation ?? null,
    gender: performer.gender ?? null,
    birthdate: performer.birth_date ?? null,
    country: performer.country ?? null,
    ethnicity: performer.ethnicity ?? null,
    eyeColor: performer.eye_color ?? null,
    hairColor: performer.hair_color ?? null,
    height: performer.height != null ? String(performer.height) : null,
    weight: null, // StashBox doesn't have a weight field
    measurements,
    tattoos,
    piercings,
    aliases: performer.aliases?.join(", ") || null,
    details: null, // StashBox performers don't have a details/bio field
    imageUrl: imageUrls[0] ?? null,
    imageUrls,
    tagNames: [], // StashBox performers don't have tags
  };
}

/**
 * Convert a StashBox scene to a raw result shape compatible with
 * StashScrapedScene, so the existing accept flow can extract studio
 * URL/image/parent data for enrichment.
 */
export function stashBoxSceneToRawResult(scene: StashBoxScene): Record<string, unknown> {
  return {
    // Remote StashBox scene ID — read back at accept time to auto-link the
    // local scene to its remote fingerprint submission target.
    id: scene.id,
    title: scene.title,
    code: scene.code,
    details: scene.details,
    director: scene.director,
    date: scene.date,
    url: scene.urls?.[0]?.url,
    urls: scene.urls?.map((u) => u.url),
    image: scene.images?.[0]?.url,
    duration: scene.duration,
    studio: scene.studio
      ? {
          name: scene.studio.name,
          url: scene.studio.urls?.[0]?.url,
          urls: scene.studio.urls?.map((u) => u.url),
          image: scene.studio.images?.[0]?.url,
          parent: scene.studio.parent
            ? { name: scene.studio.parent.name }
            : undefined,
        }
      : undefined,
    performers: scene.performers?.map((pa) => ({
      name: pa.performer.name,
      disambiguation: pa.performer.disambiguation,
      gender: pa.performer.gender,
      urls: pa.performer.urls?.map((u) => u.url),
      birthdate: pa.performer.birth_date,
      country: pa.performer.country,
      ethnicity: pa.performer.ethnicity,
      eye_color: pa.performer.eye_color,
      hair_color: pa.performer.hair_color,
      height: pa.performer.height != null ? String(pa.performer.height) : undefined,
      aliases: pa.performer.aliases?.join(", "),
      tattoos: pa.performer.tattoos
        ?.map((t) => [t.location, t.description].filter(Boolean).join(": "))
        .join("; "),
      piercings: pa.performer.piercings
        ?.map((p) => [p.location, p.description].filter(Boolean).join(": "))
        .join("; "),
      image: pa.performer.images?.[0]?.url,
      images: pa.performer.images?.map((img) => img.url),
    })),
    tags: scene.tags?.map((t) => ({ name: t.name })),
  };
}

// ─── Helpers ───────────────────────────────────────────────────────

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

function formatMeasurements(
  m: StashBoxPerformer["measurements"] | null | undefined,
): string | null {
  if (!m) return null;
  const parts: string[] = [];
  if (m.band_size && m.cup_size) {
    parts.push(`${m.band_size}${m.cup_size}`);
  }
  if (m.waist) parts.push(`${m.waist}`);
  if (m.hip) parts.push(`${m.hip}`);
  return parts.length > 0 ? parts.join("-") : null;
}
