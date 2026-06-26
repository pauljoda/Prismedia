import {
  ACQUISITION_STATUS,
  CAPABILITY_KIND,
  ENTITY_FILE_ROLE,
  ENTITY_KIND,
  REQUEST_MEDIA_KIND,
  type AcquisitionStatusCode,
  type RequestHistoryStatusCode,
} from "$lib/api/generated/codes";
import type {
  AcquisitionSummary,
  EntityCapability,
  EntityCard,
  EntityKind,
  RequestHistoryEntry,
  RequestSearchResult,
} from "$lib/api/generated/model";
import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
import { numericValue, trackedLabel } from "$lib/requests/request-helpers";

/** Library entity kind that best represents a request media kind, for poster aspect + placeholder family. */
export const ENTITY_KIND_FOR_REQUEST: Record<string, EntityKind> = {
  [REQUEST_MEDIA_KIND.movie]: ENTITY_KIND.movie,
  [REQUEST_MEDIA_KIND.series]: ENTITY_KIND.videoSeries,
  [REQUEST_MEDIA_KIND.artist]: ENTITY_KIND.musicArtist,
  [REQUEST_MEDIA_KIND.album]: ENTITY_KIND.audioLibrary,
  [REQUEST_MEDIA_KIND.book]: ENTITY_KIND.book,
  [REQUEST_MEDIA_KIND.plugin]: ENTITY_KIND.book,
};

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

const REQUEST_HISTORY_STATUS_LABEL: Record<string, string> = {
  submitted: "Submitted",
  pending: "Pending",
  downloading: "Downloading",
  partial: "Partial",
  available: "Available",
  removed: "Removed",
  unknown: "Unknown",
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

function withStatus(card: EntityThumbnailCard, statusLabel: string, subtitle: string | null): EntityThumbnailCard {
  return {
    ...card,
    subtitle: subtitle ?? undefined,
    custom: { bottomLeft: { label: statusLabel } },
  };
}

/** A Prismedia acquisition rendered as a synthetic EntityThumbnail for the review grid. */
export function acquisitionToThumbnailCard(item: AcquisitionSummary): EntityThumbnailCard {
  const card = entityCardToThumbnailCard(
    syntheticEntityCard(item.id, ENTITY_KIND.book, item.title, item.posterUrl),
    `/request/acquisition/${item.id}`,
  );
  const subtitle = [item.author, item.year ? String(item.year) : null].filter(Boolean).join(" · ") || null;
  return withStatus(card, ACQUISITION_STATUS_LABEL[item.status] ?? item.status, subtitle);
}

/** An *arr request-history entry rendered as a synthetic EntityThumbnail for the review grid. */
export function requestHistoryToThumbnailCard(entry: RequestHistoryEntry): EntityThumbnailCard {
  const kind = ENTITY_KIND_FOR_REQUEST[entry.kind] ?? ENTITY_KIND.video;
  const params = new URLSearchParams({ source: entry.source });
  if (entry.serviceId) params.set("serviceId", entry.serviceId);
  const href = `/request/${entry.kind}/${encodeURIComponent(entry.externalId)}?${params.toString()}`;

  const card = entityCardToThumbnailCard(syntheticEntityCard(entry.id, kind, entry.title, entry.posterUrl), href);
  const status =
    REQUEST_HISTORY_STATUS_LABEL[String(entry.status)] ?? String(entry.status);
  return withStatus(card, status, entry.subtitle ?? entry.serviceName);
}

/** A provider search result rendered as a synthetic EntityThumbnail for the Discover grid. */
export function requestSearchResultToThumbnailCard(result: RequestSearchResult, href: string): EntityThumbnailCard {
  const kind = ENTITY_KIND_FOR_REQUEST[result.kind] ?? ENTITY_KIND.video;
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
    // Surface "already in Radarr/Sonarr/…" as the corner overlay, mirroring library cards.
    custom: result.tracked ? { bottomLeft: { label: trackedLabel(result.source) } } : undefined,
  };
}

/** Status codes that mean an acquisition is still mid-flight (drives live polling). */
export const ACTIVE_ACQUISITION_STATUSES: AcquisitionStatusCode[] = [
  ACQUISITION_STATUS.pending,
  ACQUISITION_STATUS.searching,
  ACQUISITION_STATUS.queued,
  ACQUISITION_STATUS.downloading,
  ACQUISITION_STATUS.importing,
];
