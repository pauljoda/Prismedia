import {
  ACQUISITION_STATUS,
  CAPABILITY_KIND,
  ENTITY_FILE_ROLE,
  ENTITY_KIND,
  REQUEST_HISTORY_STATUS,
  REQUEST_MEDIA_KIND,
  type AcquisitionStatusCode,
  type RequestHistoryStatusCode,
  type RequestMediaKindCode,
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
  [REQUEST_MEDIA_KIND.author]: ENTITY_KIND.bookAuthor,
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

/**
 * Status groups for the review queue, ordered so the user sees what needs them first. "action" floats
 * to the top, then live "progress", then settled "done".
 */
export type ReviewGroup = "action" | "progress" | "completed" | "cancelled";

export const REVIEW_GROUP_ORDER: ReviewGroup[] = ["action", "progress", "completed", "cancelled"];

export const REVIEW_GROUP_LABELS: Record<ReviewGroup, string> = {
  action: "Needs your attention",
  progress: "In progress",
  completed: "Completed",
  cancelled: "Cancelled",
};

const ACQUISITION_GROUP: Record<string, ReviewGroup> = {
  [ACQUISITION_STATUS.awaitingSelection]: "action",
  [ACQUISITION_STATUS.manualImportRequired]: "action",
  [ACQUISITION_STATUS.failed]: "action",
  [ACQUISITION_STATUS.pending]: "progress",
  [ACQUISITION_STATUS.searching]: "progress",
  [ACQUISITION_STATUS.queued]: "progress",
  [ACQUISITION_STATUS.downloading]: "progress",
  [ACQUISITION_STATUS.downloaded]: "progress",
  [ACQUISITION_STATUS.importing]: "progress",
  [ACQUISITION_STATUS.imported]: "completed",
  [ACQUISITION_STATUS.cancelled]: "cancelled",
};

const HISTORY_GROUP: Record<string, ReviewGroup> = {
  [REQUEST_HISTORY_STATUS.submitted]: "progress",
  [REQUEST_HISTORY_STATUS.pending]: "progress",
  [REQUEST_HISTORY_STATUS.downloading]: "progress",
  [REQUEST_HISTORY_STATUS.partial]: "progress",
  [REQUEST_HISTORY_STATUS.available]: "completed",
  [REQUEST_HISTORY_STATUS.removed]: "cancelled",
  [REQUEST_HISTORY_STATUS.unknown]: "cancelled",
};

/** Which subsystem owns an item, so the queue removes it through the right endpoint. */
export type ReviewItemType = "acquisition" | "history";

/** A request/acquisition normalized for the grouped review queue: its card plus the metadata the queue groups, sorts, and removes by. */
export interface ReviewItem {
  id: string;
  type: ReviewItemType;
  kind: RequestMediaKindCode;
  group: ReviewGroup;
  statusLabel: string;
  /** ISO timestamp the item was created/requested, for date-added-descending sort. */
  createdAt: string;
  card: EntityThumbnailCard;
}

/** A Prismedia acquisition normalized into a review-queue item. */
export function acquisitionToReviewItem(item: AcquisitionSummary): ReviewItem {
  const status = item.status as AcquisitionStatusCode;
  const base = ACQUISITION_STATUS_LABEL[status] ?? status;
  const statusLabel =
    status === ACQUISITION_STATUS.downloading && item.progress != null
      ? `${base} · ${Math.round(Number(item.progress) * 100)}%`
      : base;
  const card = entityCardToThumbnailCard(
    syntheticEntityCard(item.id, ENTITY_KIND.book, item.title, item.posterUrl),
    `/request/acquisition/${item.id}`,
  );
  return {
    id: item.id,
    type: "acquisition",
    kind: REQUEST_MEDIA_KIND.book,
    group: ACQUISITION_GROUP[status] ?? "progress",
    statusLabel,
    createdAt: item.createdAt,
    card: { ...card, subtitle: statusLabel },
  };
}

/** An *arr request-history entry normalized into a review-queue item. */
export function requestHistoryToReviewItem(entry: RequestHistoryEntry): ReviewItem {
  const kind = ENTITY_KIND_FOR_REQUEST[entry.kind] ?? ENTITY_KIND.video;
  const params = new URLSearchParams({ source: entry.source });
  if (entry.serviceId) params.set("serviceId", entry.serviceId);
  const href = `/request/${entry.kind}/${encodeURIComponent(entry.externalId)}?${params.toString()}`;
  const status = String(entry.status);
  const statusLabel = REQUEST_HISTORY_STATUS_LABEL[status] ?? status;
  const card = entityCardToThumbnailCard(syntheticEntityCard(entry.id, kind, entry.title, entry.posterUrl), href);
  return {
    id: entry.id,
    type: "history",
    kind: entry.kind as RequestMediaKindCode,
    group: HISTORY_GROUP[status] ?? "progress",
    statusLabel,
    createdAt: entry.requestedAt,
    card: { ...card, subtitle: `${statusLabel} · ${entry.serviceName}` },
  };
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
