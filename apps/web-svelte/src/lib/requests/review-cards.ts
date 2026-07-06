import {
  ACQUISITION_STATUS,
  CAPABILITY_KIND,
  ENTITY_FILE_ROLE,
  ENTITY_KIND,
  type AcquisitionStatusCode,
} from "$lib/api/generated/codes";
import type {
  EntityCapability,
  EntityCard,
  EntityKind,
  RequestCastMember,
  RequestChildOption,
  RequestSearchResult,
} from "$lib/api/generated/model";
import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
import { entityKindForRequest, numericValue, trackedLabel } from "$lib/requests/request-helpers";

export function acquisitionStatusLabel(status: AcquisitionStatusCode): string {
  return ACQUISITION_STATUS_LABEL[status] ?? status;
}

const ACQUISITION_STATUS_LABEL: Record<AcquisitionStatusCode, string> = {
  [ACQUISITION_STATUS.pending]: "Pending",
  [ACQUISITION_STATUS.searching]: "Searching",
  [ACQUISITION_STATUS.awaitingSelection]: "Choose release",
  [ACQUISITION_STATUS.queued]: "Queued",
  [ACQUISITION_STATUS.downloading]: "Downloading",
  [ACQUISITION_STATUS.downloaded]: "Downloaded",
  [ACQUISITION_STATUS.importing]: "Importing",
  [ACQUISITION_STATUS.imported]: "Imported",
  [ACQUISITION_STATUS.failed]: "Failed",
  [ACQUISITION_STATUS.cancelled]: "Cancelled",
  [ACQUISITION_STATUS.manualImportRequired]: "Manual import",
};

/** Builds a synthetic, presentation-only entity card carrying just a poster image. */
function syntheticEntityCard(id: string, kind: EntityKind, title: string, posterUrl: string | null): EntityCard {
  const capabilities: EntityCapability[] = [];
  if (posterUrl) {
    capabilities.push({
      kind: CAPABILITY_KIND.images,
      supportedKinds: [],
      thumbnailUrl: posterUrl,
      coverUrl: posterUrl,
      items: [{ kind: ENTITY_FILE_ROLE.poster, path: posterUrl, mimeType: "image/jpeg" }],
    } as EntityCapability);
  }

  return {
    id,
    kind,
    title,
    parentEntityId: null,
    sortOrder: null,
    capabilities,
    childrenByKind: [],
    relationships: [],
  } as EntityCard;
}

/** A provider search result rendered as a synthetic EntityThumbnail for the Discover grid. */
export function requestSearchResultToThumbnailCard(result: RequestSearchResult, href: string): EntityThumbnailCard {
  const kind = entityKindForRequest(result.kind);
  const card = entityCardToThumbnailCard(syntheticEntityCard(result.externalId, kind, result.title, result.posterUrl), href);

  const meta: NonNullable<EntityThumbnailCard["meta"]> = [];
  const year = numericValue(result.year);
  if (year) {
    meta.push({ icon: "calendar", label: String(year) });
  }
  const rating = numericValue(result.rating);
  if (rating) {
    meta.push({ icon: "count", label: rating.toFixed(1) });
  }

  return {
    ...card,
    subtitle: result.subtitle ?? undefined,
    meta: meta.length > 0 ? meta : undefined,
    // Surface an "already in library" indicator as the corner overlay when the result is tracked.
    custom: result.tracked ? { bottomLeft: { label: trackedLabel(result.source) } } : undefined,
  };
}

/**
 * A selectable child work (an author's book, an artist's album, a series' season) rendered as a
 * synthetic EntityThumbnail for the request review grid, in the shape of the entity kind it becomes.
 * Keyed by the child's provider-qualified id so selection maps back to it.
 */
export function requestChildToThumbnailCard(child: RequestChildOption): EntityThumbnailCard {
  const card = entityCardToThumbnailCard(
    syntheticEntityCard(child.id, entityKindForRequest(child.kind), child.title, child.posterUrl),
  );
  const year = numericValue(child.year);
  return {
    ...card,
    meta: year ? [{ icon: "calendar", label: String(year) }] : undefined,
  };
}

/**
 * Cast members rendered as link-less person thumbnails for the shared cast strip — virtual people
 * with the headshot and role line the provider supplied, in the same 4:5 frame real credits use.
 */
export function requestCastToThumbnailCards(cast: RequestCastMember[]): EntityThumbnailCard[] {
  return cast.map((member, index) => {
    const card = entityCardToThumbnailCard(
      syntheticEntityCard(`request-cast-${index}-${member.name}`, ENTITY_KIND.person, member.name, member.imageUrl ?? null),
    );
    // An empty href renders a non-link card: these people don't exist in the library (yet).
    return { ...card, href: "", subtitle: member.role ?? undefined };
  });
}

/** Studios rendered as link-less wide thumbnails for the shared studio rail. */
export function requestStudiosToThumbnailCards(studios: string[]): EntityThumbnailCard[] {
  return studios.map((name, index) => {
    const card = entityCardToThumbnailCard(
      syntheticEntityCard(`request-studio-${index}-${name}`, ENTITY_KIND.studio, name, null),
    );
    return { ...card, href: "" };
  });
}

/** Status codes that mean an acquisition is still mid-flight (drives live polling). */
export const ACTIVE_ACQUISITION_STATUSES: AcquisitionStatusCode[] = [
  ACQUISITION_STATUS.pending,
  ACQUISITION_STATUS.searching,
  ACQUISITION_STATUS.queued,
  ACQUISITION_STATUS.downloading,
  ACQUISITION_STATUS.importing,
];
