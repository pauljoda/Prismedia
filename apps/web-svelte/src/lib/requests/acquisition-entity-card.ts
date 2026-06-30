import { CAPABILITY_KIND, ENTITY_FILE_ROLE, ENTITY_KIND } from "$lib/api/generated/codes";
import type { AcquisitionSummary, EntityCapability } from "$lib/api/generated/model";
import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";

/**
 * Builds a synthetic EntityDetail card for a Prismedia acquisition so its detail
 * page renders through the shared EntityDetail surface, exactly like a library
 * book. Acquisition-specific concepts (status, release candidates, queue/cancel)
 * layer in through the page's snippets and action buttons.
 */
export function acquisitionToDetailCard(item: AcquisitionSummary): EntityDetailCardFull {
  const capabilities: EntityCapability[] = [];
  if (item.posterUrl) {
    capabilities.push({
      kind: CAPABILITY_KIND.images,
      supportedKinds: [],
      thumbnailUrl: item.posterUrl,
      coverUrl: item.posterUrl,
      items: [{ kind: ENTITY_FILE_ROLE.poster, path: item.posterUrl, mimeType: "image/jpeg" }],
    } as EntityCapability);
  }
  if (item.description) {
    capabilities.push({ kind: CAPABILITY_KIND.description, value: item.description } as EntityCapability);
  }

  return entityCardToDetailCard({
    id: `acquisition-${item.id}`,
    kind: ENTITY_KIND.book,
    title: item.title,
    parentEntityId: null,
    sortOrder: null,
    capabilities,
    childrenByKind: [],
    relationships: [],
  });
}
