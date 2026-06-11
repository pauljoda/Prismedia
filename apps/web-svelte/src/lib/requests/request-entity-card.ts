import { CAPABILITY_KIND, ENTITY_FILE_ROLE, ENTITY_KIND, REQUEST_MEDIA_KIND } from "$lib/api/generated/codes";
import type { EntityCapability, EntityKind } from "$lib/api/generated/model";
import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
import type { RequestDetailResponse } from "./request-model";

/**
 * Library entity kind that best represents a request media kind, so the
 * EntityDetail surface lays the synthetic card out like its library sibling
 * (poster aspect, placeholder family).
 */
const ENTITY_KIND_FOR_REQUEST: Record<string, EntityKind> = {
  [REQUEST_MEDIA_KIND.movie]: ENTITY_KIND.movie,
  [REQUEST_MEDIA_KIND.series]: ENTITY_KIND.videoSeries,
  [REQUEST_MEDIA_KIND.artist]: ENTITY_KIND.musicArtist,
  [REQUEST_MEDIA_KIND.album]: ENTITY_KIND.audioLibrary,
};

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

  const card = entityCardToDetailCard({
    id: `request-${detail.source}-${detail.externalId}`,
    kind: ENTITY_KIND_FOR_REQUEST[detail.kind] ?? ENTITY_KIND.video,
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
