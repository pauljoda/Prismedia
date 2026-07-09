import { REQUEST_MEDIA_KIND } from "$lib/api/generated/codes";
import type { ExternalIdentity, RequestReviewResponse } from "$lib/api/generated/model";
import { externalIdentities, firstExternalIdentity } from "$lib/api/capabilities";
import { isRelationshipKind } from "$lib/components/identify-review";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
import { numericValue } from "$lib/requests/request-helpers";

export interface SeasonPassRow {
  /** Stable list key: the local Entity id when known, otherwise the plugin proposal id. */
  key: string;
  /** Existing Prismedia season Entity id; null for provider-only seasons. */
  entityId: string | null;
  /** Direct structural proposal selected by a reviewed commit; null for local-only seasons. */
  proposalId: string | null;
  /** Persistent provider identity kept as a structured, opaque value. */
  externalIdentity: ExternalIdentity | null;
  title: string;
  /** Regular season number. Provider specials/season 0 are intentionally not listed. */
  number: number;
  episodes: number | null;
}

export interface BuildSeasonPassRowsInput {
  localSeasons: EntityThumbnailCard[];
  episodeCounts: Record<string, number>;
  providerReview?: RequestReviewResponse | null;
}

/**
 * Combines local season Entities with direct, independently identifiable season proposals from the
 * canonical provider review. Matching compares namespace/value objects and never parses a qualified
 * string, so opaque values may contain colons and mixed case safely.
 */
export function buildSeasonPassRows({
  localSeasons,
  episodeCounts,
  providerReview,
}: BuildSeasonPassRowsInput): SeasonPassRow[] {
  const usedLocalIds = new Set<string>();
  const localByNumber = new Map<number, EntityThumbnailCard>();
  for (const card of localSeasons) {
    const number = numericValue(card.entity.sortOrder);
    if (number !== null && number > 0 && !localByNumber.has(number)) {
      localByNumber.set(number, card);
    }
  }

  const targets = new Map(
    (providerReview?.targets ?? [])
      .filter((target) => target.kind === REQUEST_MEDIA_KIND.season && target.requestable)
      .map((target) => [target.proposalId, target] as const),
  );
  const rows: SeasonPassRow[] = [];
  for (const proposal of providerReview?.proposal.children ?? []) {
    if (isRelationshipKind(proposal.targetKind)) continue;
    const target = targets.get(proposal.proposalId);
    if (!target) continue;
    const number = numericValue(target.position);
    if (number === null || number <= 0) continue;

    const local = localSeasons.find((card) =>
      externalIdentities(card.entity.capabilities).some((identity) =>
        sameIdentity(identity, target.externalIdentity),
      ),
    ) ?? localByNumber.get(number) ?? null;
    if (local) usedLocalIds.add(local.entity.id);
    const structuralChildren = proposal.children.filter((child) => !isRelationshipKind(child.targetKind));
    const providerEpisodeCount = structuralChildren.length > 0 ? structuralChildren.length : null;

    rows.push({
      key: local?.entity.id ?? proposal.proposalId,
      entityId: local?.entity.id ?? null,
      proposalId: proposal.proposalId,
      externalIdentity: target.externalIdentity,
      title: local?.entity.title ?? (proposal.patch.title?.trim() || target.externalIdentity.value),
      number,
      episodes: local ? episodeCounts[local.entity.id] ?? providerEpisodeCount : providerEpisodeCount,
    });
  }

  // Local-only seasons remain actionable through their Entity ids even if the provider is down or no
  // longer returns them.
  for (const card of localSeasons) {
    if (usedLocalIds.has(card.entity.id)) continue;
    const number = numericValue(card.entity.sortOrder);
    if (number === null || number <= 0) continue;
    rows.push({
      key: card.entity.id,
      entityId: card.entity.id,
      proposalId: null,
      externalIdentity: firstExternalIdentity(card.entity.capabilities),
      title: card.entity.title,
      number,
      episodes: episodeCounts[card.entity.id] ?? null,
    });
  }

  return rows.sort((left, right) => left.number - right.number || left.title.localeCompare(right.title));
}

function sameIdentity(left: ExternalIdentity, right: ExternalIdentity): boolean {
  return left.namespace === right.namespace && left.value === right.value;
}
