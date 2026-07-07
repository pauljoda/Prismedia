import type { RequestChildOption } from "$lib/api/generated/model";
import { firstProviderQualifiedId } from "$lib/api/capabilities";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
import { numericValue } from "$lib/requests/request-helpers";

export interface SeasonPassRow {
  /** Stable list key: the local entity id when known, otherwise the provider-qualified id. */
  key: string;
  /** Existing Prismedia season entity id; null for provider-only seasons not materialized locally yet. */
  entityId: string | null;
  /** Provider-qualified id ("provider:itemId") used to request provider-only seasons. */
  externalId: string | null;
  title: string;
  /** Regular season number. Provider specials/season 0 are intentionally not listed. */
  number: number;
  episodes: number | null;
}

export interface BuildSeasonPassRowsInput {
  localSeasons: EntityThumbnailCard[];
  episodeCounts: Record<string, number>;
  providerChildren?: RequestChildOption[] | null;
}

/**
 * Builds the Season Pass rows from both sides of the world:
 * - local season entities already in Prismedia (owned/wanted/imported)
 * - provider season options that may not be materialized locally yet
 *
 * Provider-only regular seasons must still be listed so the user can request gaps like "Daniel Tiger"
 * seasons 6/7 even when the live library only has seasons 1-5. Specials/season 0 are left out of this
 * bulk season-pass editor; they remain discoverable from the broader request/detail flow.
 */
export function buildSeasonPassRows({
  localSeasons,
  episodeCounts,
  providerChildren,
}: BuildSeasonPassRowsInput): SeasonPassRow[] {
  const localByExternal = new Map<string, EntityThumbnailCard>();
  const localByNumber = new Map<number, EntityThumbnailCard>();
  const usedLocalIds = new Set<string>();

  for (const card of localSeasons) {
    const externalId = firstProviderQualifiedId(card.entity.capabilities);
    if (externalId) localByExternal.set(externalId, card);
    const number = numericValue(card.entity.sortOrder);
    if (number !== null && number > 0 && !localByNumber.has(number)) {
      localByNumber.set(number, card);
    }
  }

  const rows: SeasonPassRow[] = [];
  for (const child of providerChildren ?? []) {
    if (child.kind !== "season" || !child.requestable) continue;
    const number = numericValue(child.number);
    // TMDB-style specials are season 0; the backend exposes them as unnumbered for preset safety.
    if (number === null || number <= 0) continue;

    const seasonNumber = number;
    const local = localByExternal.get(child.id) ?? localByNumber.get(seasonNumber) ?? null;
    if (local) usedLocalIds.add(local.entity.id);
    const itemCount = numericValue(child.itemCount);
    rows.push({
      key: local?.entity.id ?? child.id,
      entityId: local?.entity.id ?? null,
      externalId: child.id,
      title: local?.entity.title ?? child.title,
      number: seasonNumber,
      episodes: local ? episodeCounts[local.entity.id] ?? itemCount : itemCount,
    });
  }

  // Keep local-only seasons too (for providers with partial metadata or if a season was scanned but no
  // longer appears upstream). They are still actionable by entity id.
  for (const card of localSeasons) {
    if (usedLocalIds.has(card.entity.id)) continue;
    const number = numericValue(card.entity.sortOrder);
    if (number === null || number <= 0) continue;
    rows.push({
      key: card.entity.id,
      entityId: card.entity.id,
      externalId: firstProviderQualifiedId(card.entity.capabilities),
      title: card.entity.title,
      number,
      episodes: episodeCounts[card.entity.id] ?? null,
    });
  }

  return rows.sort((a, b) => a.number - b.number || a.title.localeCompare(b.title));
}
