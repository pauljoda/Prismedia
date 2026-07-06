import type { Component } from "svelte";
import { Ban, BellOff, RotateCw, Search, Trash2, X } from "@lucide/svelte";
import {
  ACQUISITION_STATUS,
  type AcquisitionStatusCode,
  type EntityKindCode,
} from "$lib/api/generated/codes";
import type {
  DownloadQueueItemView,
  EntityThumbnail,
  WantedListItemView,
} from "$lib/api/generated/model";
import { resolveEntityHref } from "$lib/entities/entity-codes";
import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
import {
  entityReferenceToThumbnailCard,
  type EntityThumbnailCard,
} from "$lib/entities/entity-thumbnail";
import { acquisitionStatusLabel } from "$lib/requests/review-cards";
import { assetUrl } from "$lib/api/orval-fetch";
import { formatBytes, formatEta, formatRelativeTime, formatSpeed } from "$lib/utils/format";

/**
 * The presentation tone of an acquisition row, driving its status pill colour, progress treatment,
 * and card accent. Kept small and semantic so the shared card never branches on raw status codes.
 */
export type AcquisitionItemTone = "downloading" | "searching" | "attention" | "done" | "muted";

/** One compact metadata chip on a card (speed, ETA, size, client, cadence, quality). */
export interface AcquisitionItemMeta {
  icon?: Component;
  label: string;
  /** Emphasis: accent for the good/primary value, danger for a problem, default otherwise. */
  tone?: "default" | "accent" | "danger";
  title?: string;
}

/** One action button on a card. Icon-first so it collapses to an icon button on mobile. */
export interface AcquisitionItemAction {
  id: string;
  label: string;
  icon: Component;
  tone?: "default" | "primary" | "danger";
  disabled?: boolean;
  run: () => void;
}

/**
 * The normalized row every acquisition-style list renders — Downloads, Missing, and Cutoff Unmet all
 * map their API rows into this one shape, so the shared card and list own the design and each tab is a
 * thin mapper. Presentation only: identity, artwork, status, an optional progress reading, metadata
 * chips, and the row's actions.
 */
export interface AcquisitionListItem {
  /** Selection key and stable list key — the monitor id for Wanted rows, the acquisition id for Downloads. */
  id: string;
  entityId?: string | null;
  kind: EntityKindCode;
  title: string;
  /** Poster thumbnail card for the artwork anchor; links to the entity page when one resolves. */
  thumbnail: EntityThumbnailCard;
  /** Entity detail link, or undefined when the item has no library home yet. */
  href?: string;
  statusLabel: string;
  tone: AcquisitionItemTone;
  /** 0..1 determinate progress for a filled bar; null when there is nothing measured. */
  progress: number | null;
  /** Show an animated indeterminate bar (searching / queued / actively hunting a release). */
  indeterminate: boolean;
  /** A one-line detail under the status — a failure reason, mostly. */
  message?: string | null;
  meta: AcquisitionItemMeta[];
  actions: AcquisitionItemAction[];
}

/** Maps an acquisition lifecycle status to a presentation tone. */
function toneForStatus(status: AcquisitionStatusCode): AcquisitionItemTone {
  switch (status) {
    case ACQUISITION_STATUS.downloading:
    case ACQUISITION_STATUS.downloaded:
    case ACQUISITION_STATUS.importing:
      return "downloading";
    case ACQUISITION_STATUS.pending:
    case ACQUISITION_STATUS.searching:
    case ACQUISITION_STATUS.queued:
      return "searching";
    case ACQUISITION_STATUS.awaitingSelection:
    case ACQUISITION_STATUS.failed:
    case ACQUISITION_STATUS.manualImportRequired:
      return "attention";
    case ACQUISITION_STATUS.imported:
      return "done";
    default:
      return "muted";
  }
}

/** True while the download client is fetching but reports no measurable progress yet. */
function isIndeterminate(status: AcquisitionStatusCode): boolean {
  return (
    status === ACQUISITION_STATUS.pending ||
    status === ACQUISITION_STATUS.searching ||
    status === ACQUISITION_STATUS.queued
  );
}

/**
 * Resolves the row's thumbnail card. When the real library entity's thumbnail has been fetched, its
 * card is used exactly as the grid builds it — proper cover art and the kind's own frame shape (a book
 * is tall, an album square, a video wide). Only a row with no library entity yet (a bare ad-hoc
 * acquisition) falls back to a poster reference from the acquisition's captured art.
 */
function thumbnailFor(
  fallbackId: string,
  kind: EntityKindCode,
  title: string,
  posterUrl: string | null | undefined,
  href: string | undefined,
  resolved: EntityThumbnail | null | undefined,
): EntityThumbnailCard {
  if (resolved) {
    return entityCardToThumbnailCard(resolved, href);
  }
  return entityReferenceToThumbnailCard(
    { id: fallbackId, kind, title, thumbnailUrl: posterUrl ? assetUrl(posterUrl) : null },
    { href },
  );
}

/** Callbacks the Downloads tab supplies; the mapper decides which appear per status. */
export interface DownloadItemCallbacks {
  onReSearch: (item: DownloadQueueItemView) => void;
  onRemove: (item: DownloadQueueItemView) => void;
}

/** Maps a global Downloads row into the shared list item, wiring its per-status actions and telemetry chips. */
export function downloadToListItem(
  row: DownloadQueueItemView,
  thumbnail: EntityThumbnail | null,
  callbacks: DownloadItemCallbacks,
  acting: boolean,
): AcquisitionListItem {
  const status = row.status as AcquisitionStatusCode;
  const href = row.entityId ? resolveEntityHref(row.kind, row.entityId) : undefined;
  // A determinate bar belongs only to an actively-transferring item; an awaiting/failed row at 0% would
  // otherwise render a misleading empty bar. Searching/queued get the indeterminate sweep instead.
  const showsProgress =
    status === ACQUISITION_STATUS.downloading ||
    status === ACQUISITION_STATUS.downloaded ||
    status === ACQUISITION_STATUS.importing;
  const progress = showsProgress && row.progress != null && Number.isFinite(Number(row.progress)) ? Number(row.progress) : null;

  const meta: AcquisitionItemMeta[] = [];
  if (row.downloadSpeedBytesPerSecond != null && Number(row.downloadSpeedBytesPerSecond) > 0) {
    meta.push({ label: formatSpeed(Number(row.downloadSpeedBytesPerSecond)), tone: "accent" });
  }
  if (row.etaSeconds != null && Number(row.etaSeconds) > 0) {
    meta.push({ label: formatEta(Number(row.etaSeconds)) });
  }
  if (row.totalSizeBytes != null && Number(row.totalSizeBytes) > 0) {
    meta.push({ label: formatBytes(Number(row.totalSizeBytes)) });
  }
  if (row.clientName) {
    meta.push({ label: row.clientName, title: "Download client" });
  }

  const actions: AcquisitionItemAction[] = [];
  if (status === ACQUISITION_STATUS.awaitingSelection || status === ACQUISITION_STATUS.failed) {
    actions.push({
      id: "search",
      label: "Search again",
      icon: Search,
      tone: "primary",
      disabled: acting,
      run: () => callbacks.onReSearch(row),
    });
  }
  actions.push({
    id: "remove",
    label: "Remove",
    icon: X,
    tone: "danger",
    disabled: acting,
    run: () => callbacks.onRemove(row),
  });

  return {
    id: row.acquisitionId,
    entityId: row.entityId,
    kind: row.kind as EntityKindCode,
    title: row.title,
    thumbnail: thumbnailFor(row.entityId ?? row.acquisitionId, row.kind as EntityKindCode, row.title, row.posterUrl, href, thumbnail),
    href,
    statusLabel: acquisitionStatusLabel(status),
    tone: toneForStatus(status),
    progress,
    indeterminate: isIndeterminate(status),
    message: status === ACQUISITION_STATUS.failed ? row.statusMessage : row.transferState,
    meta,
    actions,
  };
}

/** Callbacks the Wanted lists supply for their per-row actions. */
export interface WantedItemCallbacks {
  onSearchNow: (item: WantedListItemView) => void;
  onUnmonitor: (item: WantedListItemView) => void;
}

/** A compact future ETA ("in 3h", "due") for a wanted item's next scheduled search. */
function nextSearchLabel(value: string | null | undefined): string {
  if (!value) return "due now";
  const diffMs = new Date(value).getTime() - Date.now();
  if (diffMs <= 0) return "due now";
  const minutes = Math.floor(diffMs / 60_000);
  if (minutes < 60) return `in ${minutes}m`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `in ${hours}h`;
  return `in ${Math.floor(hours / 24)}d`;
}

/**
 * Maps a Wanted row (Missing or Cutoff Unmet) into the shared item. Missing rows read as actively
 * hunting (indeterminate bar); cutoff rows show the owned → cutoff quality gap. Both carry Search-now
 * and Unmonitor.
 */
export function wantedToListItem(
  row: WantedListItemView,
  variant: "missing" | "cutoffUnmet",
  thumbnail: EntityThumbnail | null,
  callbacks: WantedItemCallbacks,
  acting: boolean,
): AcquisitionListItem {
  const href = row.entityId ? resolveEntityHref(row.kind, row.entityId) : undefined;
  const acqStatus = row.acquisitionStatus as AcquisitionStatusCode | null;

  const meta: AcquisitionItemMeta[] = [];
  if (variant === "cutoffUnmet") {
    meta.push({
      label: `${row.ownedQuality ?? "—"} → ${row.cutoffQuality ?? "—"}`,
      tone: "accent",
      title: "Owned quality → cutoff quality",
    });
  }
  meta.push({ label: `last ${formatRelativeTime(row.lastSearchedAt ?? null, true)}`, title: "Last searched" });
  meta.push({ label: `next ${nextSearchLabel(row.nextSearchAt)}`, title: "Next scheduled search" });
  if (Number(row.barrenSearches) > 0) {
    meta.push({ label: `${row.barrenSearches} barren`, tone: "danger", title: "Consecutive searches that found nothing better" });
  }

  const statusLabel = acqStatus ? acquisitionStatusLabel(acqStatus) : variant === "missing" ? "Missing" : "Cutoff unmet";

  return {
    id: row.monitorId,
    entityId: row.entityId,
    kind: row.kind as EntityKindCode,
    title: row.title,
    thumbnail: thumbnailFor(row.entityId ?? row.monitorId, row.kind as EntityKindCode, row.title, row.posterUrl, href, thumbnail),
    href,
    statusLabel,
    // Missing items are being actively re-searched; cutoff items own a copy and upgrade quietly.
    tone: variant === "missing" ? "searching" : "attention",
    progress: null,
    indeterminate: variant === "missing",
    message: null,
    meta,
    actions: [
      {
        id: "search",
        label: "Search now",
        icon: RotateCw,
        tone: "primary",
        disabled: acting || !row.acquisitionId,
        run: () => callbacks.onSearchNow(row),
      },
      {
        id: "unmonitor",
        label: "Unmonitor",
        icon: BellOff,
        tone: "default",
        disabled: acting,
        run: () => callbacks.onUnmonitor(row),
      },
    ],
  };
}

/** Bulk action shown in the selection bar (operates on the selected item ids). */
export interface AcquisitionBulkAction {
  id: string;
  label: string;
  icon: Component;
  tone?: "default" | "danger";
  run: (ids: string[]) => void;
}

export { Ban, Trash2 };
