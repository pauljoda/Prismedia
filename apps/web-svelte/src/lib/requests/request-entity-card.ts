import { CAPABILITY_KIND, ENTITY_FILE_ROLE, ENTITY_KIND } from "$lib/api/generated/codes";
import type { EntityCapability } from "$lib/api/generated/model";
import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
import { entityKindForRequest, numericValue } from "./request-helpers";
import type { RequestDetailResponse } from "./request-model";

/**
 * Builds a synthetic EntityDetail card for an external request item so request
 * pages render through the shared EntityDetail surface (banner + reflection
 * hero, poster frame, description body). External items have no library
 * entity, so the card carries only presentation capabilities — artwork,
 * description, and genre tags as link-less chips — and request-specific
 * concepts (ratings, cast, request controls) layer in through snippets.
 */
export function requestDetailToEntityCard(detail: RequestDetailResponse): EntityDetailCardFull {
  const capabilities: EntityCapability[] = [];

  if (detail.backdropUrl || detail.posterUrl) {
    capabilities.push({
      kind: CAPABILITY_KIND.images,
      supportedKinds: [],
      thumbnailUrl: detail.posterUrl ?? null,
      coverUrl: detail.posterUrl ?? detail.backdropUrl ?? null,
      items: [
        ...(detail.backdropUrl
          ? [{ kind: ENTITY_FILE_ROLE.backdrop, path: detail.backdropUrl, mimeType: "image/jpeg" }]
          : []),
        ...(detail.posterUrl
          ? [{ kind: ENTITY_FILE_ROLE.poster, path: detail.posterUrl, mimeType: "image/jpeg" }]
          : []),
      ],
    } as EntityCapability);
  }

  if (detail.overview) {
    capabilities.push({ kind: CAPABILITY_KIND.description, value: detail.overview } as EntityCapability);
  }

  // Dates and runtime flow through the same capabilities a real entity carries, so EntityDetail's
  // hero meta and metadata sections light up exactly as they would after identify.
  const dateItems = Object.entries(detail.dates ?? {})
    .filter(([, value]) => Boolean(value))
    .map(([code, value]) => ({ code, value }));
  if (dateItems.length === 0 && detail.year) {
    dateItems.push({ code: "release", value: String(detail.year) });
  }
  if (dateItems.length > 0) {
    capabilities.push({ kind: CAPABILITY_KIND.dates, items: dateItems } as EntityCapability);
  }

  const runtimeMinutes = numericValue(detail.runtimeMinutes);
  if (runtimeMinutes) {
    const duration = `${Math.floor(runtimeMinutes / 60)}:${String(runtimeMinutes % 60).padStart(2, "0")}:00`;
    capabilities.push({ kind: CAPABILITY_KIND.technical, duration } as EntityCapability);
  }

  const card = entityCardToDetailCard({
    id: `request-${detail.source}-${detail.externalId}`,
    kind: entityKindForRequest(detail.kind),
    title: detail.title,
    parentEntityId: null,
    sortOrder: null,
    capabilities,
    childrenByKind: [],
    relationships: [],
  });

  return {
    ...card,
    tags: detail.tags.map((genre) => ({ id: genre, kind: ENTITY_KIND.tag, title: genre, href: null })),
  };
}
