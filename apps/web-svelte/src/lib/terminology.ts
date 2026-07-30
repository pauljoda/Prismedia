/**
 * Compatibility aliases for recurring product copy, derived from the generated Entity-kind
 * definitions rather than maintaining another label registry.
 */
import { ENTITY_KIND, displayNameForEntityKind, labelForEntityKind } from "$lib/entities/entity-codes";

export const entityTerms = {
  videos: labelForEntityKind(ENTITY_KIND.video),
  video: displayNameForEntityKind(ENTITY_KIND.video),
  performers: labelForEntityKind(ENTITY_KIND.person),
  studios: labelForEntityKind(ENTITY_KIND.studio),
  tags: labelForEntityKind(ENTITY_KIND.tag),
};

export type EntityTerms = typeof entityTerms;

export function formatVideoCount(count: number): string {
  const w =
    count === 1
      ? entityTerms.video.toLowerCase()
      : entityTerms.videos.toLowerCase();
  return `${count} ${w}`;
}

export function useTerms(): EntityTerms {
  return entityTerms;
}
