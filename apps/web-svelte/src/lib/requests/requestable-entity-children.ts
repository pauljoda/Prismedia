import { ENTITY_KINDS_SUPPORTING_REQUESTS } from "$lib/api/generated/codes";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

const REQUESTABLE_ENTITY_KINDS: readonly string[] = ENTITY_KINDS_SUPPORTING_REQUESTS;

/**
 * Returns every request-controllable direct child in the canonical ParentEntityId graph. The parent
 * descriptor's one missing-search child kind does not constrain this list: a mixed series can manage
 * seasons, direct sub-series, and loose episodes independently when each child kind is committable.
 */
export function requestableDirectChildCards(
  parentEntityId: string | null | undefined,
  cards: readonly EntityThumbnailCard[],
): EntityThumbnailCard[] {
  if (!parentEntityId) return [];

  return cards.filter((card) =>
    card.entity.parentEntityId === parentEntityId
      && isRequestableEntityKind(card.entity.kind),
  );
}

/** Whether one persisted Entity kind participates in the shared request/monitor flow. */
function isRequestableEntityKind(entityKind: string): boolean {
  return REQUESTABLE_ENTITY_KINDS.includes(entityKind);
}
