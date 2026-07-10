import type { Component } from "svelte";
import {
  BellOff,
  CircleAlert,
  CircleCheck,
  CircleX,
  CloudDownload,
  ExternalLink,
  Eye,
  Hourglass,
  LoaderCircle,
  RotateCw,
  Search,
  Trash2,
  TriangleAlert,
} from "@lucide/svelte";
import {
  ACQUISITION_STATUS,
  ENTITY_KIND,
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
import { acquisitionStatusIsKnown, acquisitionStatusLabel } from "$lib/requests/acquisition-status";
import {
  monitorHasUnknownStatus,
  monitorIsDeletingFiles,
  monitorIsStopping,
  monitorTransitionIsLocked,
} from "$lib/requests/monitor-status";
import { assetUrl } from "$lib/api/orval-fetch";
import { formatBytes, formatEta, formatRelativeTime, formatSpeed } from "$lib/utils/format";

/**
 * The presentation tone of an acquisition row, driving its status chip colour, progress treatment, and
 * left accent rail. Kept small and semantic so the shared card never branches on raw status codes.
 */
export type AcquisitionItemTone = "downloading" | "searching" | "queued" | "cleanup" | "attention" | "failed" | "done" | "muted";

/**
 * One action on a card. Rendered as a `<button>` when it carries {@link run}, or an `<a>` when it
 * carries {@link href} — so navigation and commands share the model.
 */
export interface AcquisitionItemAction {
  id: string;
  label: string;
  icon: Component;
  tone?: "default" | "primary" | "danger";
  disabled?: boolean;
  href?: string;
  run?: () => void;
}

/**
 * The normalized row every acquisition list renders — Downloads, Missing, and Cutoff Unmet all map their
 * API rows into this one shape, so the shared card and list own the design and each tab stays a thin
 * mapper. Presentation only: identity, artwork, a creator subtitle, the status chip + description, an
 * optional progress reading, a client badge with bullet-separated meta, and the row's actions.
 */
export interface AcquisitionListItem {
  /** Selection key and stable list key — the monitor id for Wanted rows, the acquisition id for Downloads. */
  id: string;
  entityId?: string | null;
  kind: EntityKindCode;
  title: string;
  /** Creator/year context under the title ("Andy Weir", "Bluey (2018)"). */
  subtitle?: string | null;
  /** Entity thumbnail card (real cover + kind shape), from the same builder the library grid uses. */
  thumbnail: EntityThumbnailCard;
  /** Entity detail link, or undefined when the item has no library home yet. */
  href?: string;
  statusLabel: string;
  statusIcon: Component;
  tone: AcquisitionItemTone;
  /** 0..1 determinate progress for a filled bar; null when there is nothing measured. */
  progress: number | null;
  /** Show an animated indeterminate bar (searching / actively hunting a release). */
  indeterminate: boolean;
  /** One-line explanation under the status chip ("Waiting for a download slot", a failure reason). */
  description?: string | null;
  /** Download client badge in the meta row, when a transfer is (or was) in flight. */
  clientLabel?: string | null;
  /** Owned → cutoff quality, shown as an accent chip on the cutoff-unmet list. */
  qualityGap?: string | null;
  /** Bullet-separated plain meta after the client badge (speed, size, ETA, cadence). */
  metaParts: string[];
  /** False while a durable transition owns the row and bulk mutation must fail closed. */
  selectable?: boolean;
  primaryAction?: AcquisitionItemAction | null;
  removeAction?: AcquisitionItemAction | null;
  menuActions: AcquisitionItemAction[];
}

/** Maps an acquisition lifecycle status to a presentation tone. */
function toneForStatus(status: AcquisitionStatusCode): AcquisitionItemTone {
  switch (status) {
    case ACQUISITION_STATUS.downloading:
    case ACQUISITION_STATUS.downloaded:
    case ACQUISITION_STATUS.importing:
      return "downloading";
    case ACQUISITION_STATUS.queued:
      return "queued";
    case ACQUISITION_STATUS.pending:
    case ACQUISITION_STATUS.searching:
      return "searching";
    case ACQUISITION_STATUS.failed:
      return "failed";
    case ACQUISITION_STATUS.awaitingSelection:
    case ACQUISITION_STATUS.manualImportRequired:
      return "attention";
    case ACQUISITION_STATUS.imported:
      return "done";
    case ACQUISITION_STATUS.stopping:
      return "cleanup";
    case ACQUISITION_STATUS.cancelled:
      return "muted";
    default:
      return "cleanup";
  }
}

/** The status chip's icon for a lifecycle status. */
function iconForStatus(status: AcquisitionStatusCode): Component {
  switch (status) {
    case ACQUISITION_STATUS.downloading:
    case ACQUISITION_STATUS.downloaded:
    case ACQUISITION_STATUS.importing:
      return CloudDownload;
    case ACQUISITION_STATUS.queued:
      return Hourglass;
    case ACQUISITION_STATUS.pending:
    case ACQUISITION_STATUS.searching:
    case ACQUISITION_STATUS.awaitingSelection:
      return Search;
    case ACQUISITION_STATUS.failed:
      return CircleAlert;
    case ACQUISITION_STATUS.manualImportRequired:
      return TriangleAlert;
    case ACQUISITION_STATUS.imported:
      return CircleCheck;
    case ACQUISITION_STATUS.stopping:
      return LoaderCircle;
    case ACQUISITION_STATUS.cancelled:
      return CircleX;
    default:
      return LoaderCircle;
  }
}

/** The one-line description under the status chip for a download row (null when the progress bar says it all). */
function downloadDescription(status: AcquisitionStatusCode, statusMessage: string | null | undefined): string | null {
  switch (status) {
    case ACQUISITION_STATUS.awaitingSelection:
      return "Select a release to start the download.";
    case ACQUISITION_STATUS.pending:
      return "Preparing to search…";
    case ACQUISITION_STATUS.searching:
      return "Finding releases…";
    case ACQUISITION_STATUS.queued:
      return "Waiting for a download slot.";
    case ACQUISITION_STATUS.failed:
      return statusMessage ?? "The download failed.";
    case ACQUISITION_STATUS.downloaded:
      return "Download complete; importing…";
    case ACQUISITION_STATUS.importing:
      return "Importing into your library…";
    case ACQUISITION_STATUS.manualImportRequired:
      return statusMessage ?? "Manual import required.";
    case ACQUISITION_STATUS.stopping:
      return "Removing download and managed files…";
    default:
      return null;
  }
}

/** True while the client is fetching but reports no measurable progress yet (the indeterminate sweep). */
function isIndeterminate(status: AcquisitionStatusCode): boolean {
  return (
    status === ACQUISITION_STATUS.pending ||
    status === ACQUISITION_STATUS.searching ||
    status === ACQUISITION_STATUS.queued ||
    status === ACQUISITION_STATUS.stopping ||
    !acquisitionStatusIsKnown(status)
  );
}

/**
 * Composes the creator/year subtitle: a TV unit leads with its series, everything else with its author
 * or artist, and the year is appended when known ("Bluey (2018)", "Andy Weir").
 */
function composeSubtitle(kind: EntityKindCode, author: string | null | undefined, series: string | null | undefined, year: number | null | undefined): string | null {
  const isTv = kind === ENTITY_KIND.videoSeason || kind === ENTITY_KIND.video || kind === ENTITY_KIND.videoSeries;
  const creator = ((isTv ? series : author) ?? author ?? series)?.trim() || null;
  const yearLabel = year ? `(${year})` : null;
  return creator && yearLabel ? `${creator} ${yearLabel}` : creator ?? yearLabel;
}

/**
 * Resolves the row's thumbnail card. When the real library entity's thumbnail has been fetched, its card
 * is used exactly as the grid builds it — proper cover art and the kind's own frame shape. Only a row
 * with no library entity yet (a bare ad-hoc acquisition) falls back to a poster reference.
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

/**
 * Resolves the entity detail link. A nested kind (a TV season, a book volume) needs its parent as route
 * context to build a valid URL; the fetched thumbnail carries that parent, so the link resolves once the
 * thumbnail loads and stays undefined for a bare acquisition with no library entity.
 */
function hrefFor(kind: EntityKindCode, entityId: string | null | undefined, resolved: EntityThumbnail | null | undefined): string | undefined {
  if (!entityId) return undefined;
  const parent =
    resolved?.parentEntityId && resolved.parentKind
      ? { kind: resolved.parentKind as EntityKindCode, id: resolved.parentEntityId }
      : undefined;
  return resolveEntityHref(kind, entityId, parent);
}

/** Callbacks the Downloads tab supplies; the mapper decides which appear per status. */
export interface DownloadItemCallbacks {
  onReSearch: (item: DownloadQueueItemView) => void;
  onRemove: (item: DownloadQueueItemView) => void;
}

/** Maps a global Downloads row into the shared list item, wiring its per-status chip, actions, and telemetry. */
export function downloadToListItem(
  row: DownloadQueueItemView,
  thumbnail: EntityThumbnail | null,
  callbacks: DownloadItemCallbacks,
  acting: boolean,
): AcquisitionListItem {
  const status = row.status as AcquisitionStatusCode;
  const kind = row.kind as EntityKindCode;
  const href = hrefFor(kind, row.entityId, thumbnail);
  const transitionLocked = status === ACQUISITION_STATUS.stopping || !acquisitionStatusIsKnown(status);

  // A determinate bar belongs only to an actively-transferring item; searching/queued get the sweep.
  const showsProgress =
    status === ACQUISITION_STATUS.downloading ||
    status === ACQUISITION_STATUS.downloaded ||
    status === ACQUISITION_STATUS.importing;
  const progress = showsProgress && row.progress != null && Number.isFinite(Number(row.progress)) ? Number(row.progress) : null;

  const metaParts: string[] = [];
  if (row.downloadSpeedBytesPerSecond != null && Number(row.downloadSpeedBytesPerSecond) > 0) {
    metaParts.push(formatSpeed(Number(row.downloadSpeedBytesPerSecond)));
  }
  const total = row.totalSizeBytes != null ? Number(row.totalSizeBytes) : 0;
  if (total > 0) {
    // Downloaded / total, mirroring a torrent client ("1.2 GB / 2.8 GB").
    metaParts.push(progress != null ? `${formatBytes(total * progress)} / ${formatBytes(total)}` : formatBytes(total));
  }
  if (row.etaSeconds != null && Number(row.etaSeconds) > 0) {
    metaParts.push(`ETA ${formatEta(Number(row.etaSeconds))}`);
  }
  // A short transfer-state word (Stalled, Paused) for a non-downloading row where it adds signal.
  if (!showsProgress && row.transferState && status !== ACQUISITION_STATUS.searching) {
    metaParts.push(row.transferState);
  }

  const searchable =
    status === ACQUISITION_STATUS.awaitingSelection ||
    status === ACQUISITION_STATUS.failed ||
    status === ACQUISITION_STATUS.searching ||
    status === ACQUISITION_STATUS.pending;

  // Primary CTA: pick a release / re-search when acted on, otherwise open the entity to manage it.
  let primaryAction: AcquisitionItemAction | null;
  if (transitionLocked) {
    primaryAction = null;
  } else if (status === ACQUISITION_STATUS.awaitingSelection && href) {
    primaryAction = { id: "choose", label: "Choose release", icon: Search, tone: "primary", href };
  } else if (status === ACQUISITION_STATUS.failed || status === ACQUISITION_STATUS.searching || status === ACQUISITION_STATUS.pending) {
    primaryAction = { id: "search", label: "Search again", icon: RotateCw, tone: "primary", disabled: acting, run: () => callbacks.onReSearch(row) };
  } else if (href) {
    primaryAction = { id: "view", label: "View", icon: Eye, tone: "primary", href };
  } else {
    primaryAction = null;
  }

  // The overflow menu holds the entity link when it isn't already the primary, plus a re-search fallback.
  const menuActions: AcquisitionItemAction[] = [];
  if (!transitionLocked && href && primaryAction?.href !== href) {
    menuActions.push({ id: "open", label: "Open in library", icon: ExternalLink, href });
  }
  if (!transitionLocked && searchable && primaryAction?.id !== "search") {
    menuActions.push({ id: "research", label: "Search again", icon: RotateCw, disabled: acting, run: () => callbacks.onReSearch(row) });
  }

  return {
    id: row.acquisitionId,
    entityId: row.entityId,
    kind,
    title: row.title,
    subtitle: composeSubtitle(kind, row.author, row.series, row.year ? Number(row.year) : null),
    thumbnail: thumbnailFor(row.entityId ?? row.acquisitionId, kind, row.title, row.posterUrl, href, thumbnail),
    href,
    statusLabel: acquisitionStatusLabel(status),
    statusIcon: iconForStatus(status),
    tone: toneForStatus(status),
    progress,
    indeterminate: isIndeterminate(status),
    description: transitionLocked && status !== ACQUISITION_STATUS.stopping
      ? "Waiting for Prismedia to finish this transition…"
      : downloadDescription(status, row.statusMessage),
    clientLabel: row.clientName ?? null,
    qualityGap: null,
    metaParts,
    selectable: !transitionLocked,
    primaryAction,
    removeAction: transitionLocked
      ? null
      : { id: "remove", label: "Remove", icon: Trash2, tone: "danger", disabled: acting, run: () => callbacks.onRemove(row) },
    menuActions,
  };
}

/** Callbacks the Wanted lists supply for their per-row actions. */
export interface WantedItemCallbacks {
  onSearchNow: (item: WantedListItemView) => void;
  onUnmonitor: (item: WantedListItemView) => void;
}

interface WantedTransitionState {
  deletingFiles: boolean;
  stopping: boolean;
  unknown: boolean;
}

function wantedStatusLabel(
  acquisitionStatus: AcquisitionStatusCode | null,
  variant: "missing" | "cutoffUnmet",
  transition: WantedTransitionState,
): string {
  if (acquisitionStatus) return acquisitionStatusLabel(acquisitionStatus);
  if (transition.deletingFiles) return "Deleting files";
  if (transition.stopping) return "Stopping";
  if (transition.unknown) return "Updating";
  return variant === "missing" ? "Missing" : "Cutoff unmet";
}

function wantedDescription(
  variant: "missing" | "cutoffUnmet",
  transition: WantedTransitionState | null,
): string {
  if (!transition) {
    return variant === "missing"
      ? "Watching for a release…"
      : "Owned copy is below the quality cutoff — upgrading.";
  }
  if (transition.deletingFiles) return "Removing managed files before monitoring resumes…";
  if (transition.stopping) return "Removing pending work and wanted state…";
  return "Waiting for Prismedia to finish this transition…";
}

/** A compact future ETA ("in 3h", "due now") for a wanted item's next scheduled search. */
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
 * Maps a Wanted row (Missing or Cutoff Unmet) into the shared item. Missing rows read as actively hunting
 * (indeterminate sweep); cutoff rows surface the owned → cutoff quality gap. Both carry Search-now and
 * Unmonitor, and Open-in-library in the overflow, except while a durable transition locks the row.
 */
export function wantedToListItem(
  row: WantedListItemView,
  variant: "missing" | "cutoffUnmet",
  thumbnail: EntityThumbnail | null,
  callbacks: WantedItemCallbacks,
  acting: boolean,
): AcquisitionListItem {
  const kind = row.kind as EntityKindCode;
  const href = hrefFor(kind, row.entityId, thumbnail);
  const acqStatus = row.acquisitionStatus as AcquisitionStatusCode | null;
  const monitorState = { status: row.monitorStatus };
  const monitorDeletingFiles = monitorIsDeletingFiles(monitorState);
  const monitorStopping = monitorIsStopping(monitorState);
  const monitorUnknown = monitorHasUnknownStatus(monitorState);
  const acquisitionLocked = acqStatus !== null && (
    acqStatus === ACQUISITION_STATUS.stopping ||
    !acquisitionStatusIsKnown(acqStatus)
  );
  const transitionLocked = monitorTransitionIsLocked(monitorState) || acquisitionLocked;
  const transition = {
    deletingFiles: monitorDeletingFiles,
    stopping: monitorStopping || acqStatus === ACQUISITION_STATUS.stopping,
    unknown: monitorUnknown || (acqStatus !== null && !acquisitionStatusIsKnown(acqStatus)),
  };

  const metaParts: string[] = [
    `last ${formatRelativeTime(row.lastSearchedAt ?? null, true)}`,
    `next ${nextSearchLabel(row.nextSearchAt)}`,
  ];
  if (Number(row.barrenSearches) > 0) {
    metaParts.push(`${row.barrenSearches} barren`);
  }

  const statusLabel = wantedStatusLabel(acqStatus, variant, transition);
  const status = acqStatus ?? ACQUISITION_STATUS.searching;

  return {
    id: row.monitorId,
    entityId: row.entityId,
    kind,
    title: row.title,
    subtitle: composeSubtitle(kind, row.author, null, null),
    thumbnail: thumbnailFor(row.entityId ?? row.monitorId, kind, row.title, row.posterUrl, href, thumbnail),
    href,
    statusLabel,
    statusIcon: transitionLocked ? LoaderCircle : iconForStatus(status),
    // Missing items are actively re-searched; cutoff items own a copy and upgrade quietly.
    tone: transitionLocked ? "cleanup" : variant === "missing" ? "searching" : "attention",
    progress: null,
    indeterminate: transitionLocked || variant === "missing",
    description: wantedDescription(variant, transitionLocked ? transition : null),
    clientLabel: null,
    qualityGap: variant === "cutoffUnmet" ? `${row.ownedQuality ?? "—"} → ${row.cutoffQuality ?? "—"}` : null,
    metaParts,
    selectable: !transitionLocked,
    primaryAction: transitionLocked ? null : {
      id: "search",
      label: "Search now",
      icon: RotateCw,
      tone: "primary",
      disabled: acting || !row.acquisitionId,
      run: () => callbacks.onSearchNow(row),
    },
    removeAction: transitionLocked ? null : {
      id: "unmonitor",
      label: "Unmonitor",
      icon: BellOff,
      tone: "danger",
      disabled: acting,
      run: () => callbacks.onUnmonitor(row),
    },
    menuActions: !transitionLocked && href ? [{ id: "open", label: "Open in library", icon: ExternalLink, href }] : [],
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

export { RotateCw, Trash2 };
