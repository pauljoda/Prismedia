import type { Component } from "svelte";
import { ScanSearch, ServerOff, TriangleAlert } from "@lucide/svelte";
import {
  ENTITY_KIND,
  ENTITY_LIST_SORT,
  ENTITY_SORT_DIRECTION,
  IDENTIFY_QUEUE_STATE,
} from "$lib/api/generated/codes";
import type { DownloadQueueItemView, ListEntityShelfParams } from "$lib/api/generated/model";
import type { EntityCard } from "$lib/api/entities";
import { fetchDownloadQueue } from "$lib/api/acquisitions";
import { fetchConsumptionStatistics } from "$lib/api/consumption-statistics";
import { fetchIdentifyQueue } from "$lib/api/identify-client";
import type { IdentifyQueueItem } from "$lib/api/identify-types";
import { fetchWorkerHealth } from "$lib/api/jobs";
import { displayNameForEntityKind } from "$lib/entities/entity-codes";
import { entityCardToThumbnailCard, STATUS_FILTER_DEFS } from "$lib/entities/entity-grid";
import { resolveEntityHref } from "$lib/entities/entity-routes";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
import { fetchEntityShelfCached } from "$lib/entities/shelf-cache";
import { describeWorkerHealth, type WorkerHealthBadge } from "$lib/jobs/worker-health";
import { acquisitionStatusLabel } from "$lib/requests/acquisition-status";
import { acquisitionStatusVisual } from "$lib/requests/acquisition-status-display";
import {
  localDayKey,
  localUtcOffsetMinutes,
  statNumber,
  type ConsumptionDispersionBand,
} from "$lib/stats/consumption-stats";
import type { LibraryFamily } from "../library-composition";

// ── Shelves ────────────────────────────────────────────────────────────────

/** Engagement filter values accepted by the shelf endpoint, checked against the grid's canonical set. */
type EngagementStatus = (typeof STATUS_FILTER_DEFS)[number]["value"];
const IN_PROGRESS = "in-progress" satisfies EngagementStatus;

/** Maps shelf rows into the shared thumbnail model with their canonical links. */
export function toThumbnailCards(items: readonly EntityCard[]): EntityThumbnailCard[] {
  return items.map((item) => entityCardToThumbnailCard(item, resolveEntityHref(item.kind, item.id)));
}

/** Everything the viewer has started and not finished, most recently touched first. */
export function continueShelfParams(hideNsfw: boolean, limit = 24): ListEntityShelfParams {
  return {
    status: IN_PROGRESS,
    sort: ENTITY_LIST_SORT.lastActive,
    sortDirection: ENTITY_SORT_DIRECTION.descending,
    hideNsfw,
    limit,
  };
}

/** One family's newest items, the same query the current dashboard's family shelves use. */
export function newestShelfParams(kind: string, hideNsfw: boolean, limit = 20): ListEntityShelfParams {
  return {
    kind,
    sort: ENTITY_LIST_SORT.dateAdded,
    sortDirection: ENTITY_SORT_DIRECTION.descending,
    hideNsfw,
    limit,
  };
}

/**
 * Reads a shelf with stale-while-revalidate semantics: `apply` receives the last-minute snapshot
 * immediately when one exists, then the fresh server rows. A failed refresh keeps the snapshot.
 */
export async function readShelfCards(
  params: ListEntityShelfParams,
  apply: (cards: EntityThumbnailCard[]) => void,
): Promise<void> {
  const read = fetchEntityShelfCached(params);
  if (read.stale) apply(toThumbnailCards(read.stale.items));
  try {
    apply(toThumbnailCards((await read.fresh).items));
  } catch {
    if (!read.stale) apply([]);
  }
}

/** A newly added item and when it arrived, for merging families into one timeline. */
export interface LatestItem {
  card: EntityThumbnailCard;
  addedAt: number;
}

/**
 * The newest top-level items across every family, merged into one timeline. The unscoped shelf would
 * return chapters, tracks, and pages as separate rows, so each family is read on its own and merged.
 */
export async function loadLatestAcrossLibrary(
  kinds: readonly string[],
  hideNsfw: boolean,
  perKind = 12,
): Promise<LatestItem[]> {
  const pages = await Promise.all(
    kinds.map((kind) => {
      const read = fetchEntityShelfCached(newestShelfParams(kind, hideNsfw, perKind));
      return read.fresh.then((page) => page.items).catch(() => read.stale?.items ?? []);
    }),
  );
  return pages
    .flat()
    .map((item) => ({
      card: entityCardToThumbnailCard(item, resolveEntityHref(item.kind, item.id)),
      addedAt: Date.parse(item.createdAt ?? "") || 0,
    }))
    .sort((left, right) => right.addedAt - left.addedAt);
}

// ── Composition ────────────────────────────────────────────────────────────

/**
 * Turns library composition into prism bands. The band's share is ownership, not activity, so the
 * consumption figures stay zero and the page renders its own legend detail from `distinctEntityCount`.
 */
export function compositionBands(families: readonly LibraryFamily[]): ConsumptionDispersionBand[] {
  const present = families.filter((family) => family.count > 0);
  const total = present.reduce((sum, family) => sum + family.count, 0);
  return present.map((family) => ({
    kind: family.kind,
    label: family.label,
    accent: family.accent,
    emitted: family.emitted,
    totalEvents: 0,
    accessedCount: 0,
    completedCount: 0,
    skippedCount: 0,
    distinctEntityCount: family.count,
    activeSeconds: 0,
    share: total > 0 ? family.count / total : 0,
  }));
}

/** Everyday nouns for each library family, keyed by the family's canonical kind. */
const FAMILY_NOUNS: Record<string, readonly [singular: string, plural: string]> = {
  [ENTITY_KIND.video]: ["video", "videos"],
  [ENTITY_KIND.movie]: ["movie", "movies"],
  [ENTITY_KIND.videoSeries]: ["series", "series"],
  [ENTITY_KIND.gallery]: ["gallery", "galleries"],
  [ENTITY_KIND.book]: ["book", "books"],
  [ENTITY_KIND.comicSeries]: ["comic", "comics"],
  [ENTITY_KIND.image]: ["image", "images"],
  [ENTITY_KIND.audioLibrary]: ["album", "albums"],
};

/** Lowercase noun for a family at a given count: "album" for one, "albums" otherwise. */
export function familyNoun(kind: string, count: number): string {
  const nouns = FAMILY_NOUNS[kind];
  if (!nouns) return displayNameForEntityKind(kind).toLowerCase();
  return count === 1 ? nouns[0] : nouns[1];
}

// ── Live work ──────────────────────────────────────────────────────────────

/** Background systems the dashboard can summarize. A null section could not be read and is hidden. */
export interface LibraryPulse {
  identify: IdentifyQueueItem[] | null;
  downloads: DownloadQueueItemView[] | null;
  worker: WorkerHealthBadge | null;
}

/** Reads the Identify queue, the download queue, and the worker heartbeat in parallel. */
export async function loadLibraryPulse(hideNsfw: boolean, signal?: AbortSignal): Promise<LibraryPulse> {
  const [identify, downloads, worker] = await Promise.all([
    fetchIdentifyQueue(false, hideNsfw, { signal }).catch(() => null),
    fetchDownloadQueue().catch(() => null),
    fetchWorkerHealth({ signal }).then(describeWorkerHealth).catch(() => null),
  ]);
  return { identify, downloads, worker };
}

/** The download queue grouped by what each row is doing, using the shared lifecycle tones. */
export interface DownloadDigest {
  /** Waiting on a decision (choose release, review import). */
  attention: DownloadQueueItemView[];
  failed: DownloadQueueItemView[];
  /** Moving bytes or files right now. */
  transferring: DownloadQueueItemView[];
  /** Searching, queued, or waiting for a release or client. */
  waiting: DownloadQueueItemView[];
}

export function digestDownloads(items: readonly DownloadQueueItemView[] | null): DownloadDigest {
  const digest: DownloadDigest = { attention: [], failed: [], transferring: [], waiting: [] };
  for (const item of items ?? []) {
    const { tone } = acquisitionStatusVisual(item.status);
    if (tone === "attention") digest.attention.push(item);
    else if (tone === "failed") digest.failed.push(item);
    else if (tone === "downloading") digest.transferring.push(item);
    else if (tone === "searching" || tone === "queued") digest.waiting.push(item);
  }
  return digest;
}

/** Identify items holding a proposal the viewer has to accept or reject. */
export function identifyReviews(pulse: LibraryPulse): IdentifyQueueItem[] {
  return (pulse.identify ?? []).filter((item) => item.state === IDENTIFY_QUEUE_STATE.proposal);
}

/** One decision waiting on the viewer. */
export interface NeedsYouItem {
  key: string;
  /** The decision itself, such as "Choose release". */
  action: string;
  count: number;
  /** Title of the first waiting item, shown beside the action when only one waits; may be empty. */
  subject: string;
  href: string;
  icon: Component;
}

/**
 * Collects the work that cannot continue without the viewer: Identify proposals, acquisition
 * decisions, failed downloads, and an offline worker. Sections that could not be read contribute nothing.
 */
export function needsYouItems(pulse: LibraryPulse): NeedsYouItem[] {
  const items: NeedsYouItem[] = [];

  const reviews = identifyReviews(pulse);
  if (reviews.length > 0) {
    items.push({
      key: "identify",
      action: reviews.length === 1 ? "Review match" : "Review matches",
      count: reviews.length,
      subject: reviews[0].title,
      href: reviews.length === 1 ? `/identify/${reviews[0].entityId}` : "/identify",
      icon: ScanSearch,
    });
  }

  const digest = digestDownloads(pulse.downloads);
  const byStatus = new Map<string, DownloadQueueItemView[]>();
  for (const item of digest.attention) byStatus.set(item.status, [...(byStatus.get(item.status) ?? []), item]);
  for (const [status, rows] of byStatus) {
    items.push({
      key: `download:${status}`,
      action: acquisitionStatusLabel(status),
      count: rows.length,
      subject: rows[0].title,
      href: (rows.length === 1 && rows[0].entityId ? resolveEntityHref(rows[0].kind, rows[0].entityId) : null) ?? "/downloads",
      icon: acquisitionStatusVisual(status).icon,
    });
  }

  if (digest.failed.length > 0) {
    items.push({
      key: "download:failed",
      action: digest.failed.length === 1 ? "Check failed download" : "Check failed downloads",
      count: digest.failed.length,
      subject: digest.failed[0].title,
      href: "/downloads",
      icon: TriangleAlert,
    });
  }

  if (pulse.worker?.status === "offline") {
    items.push({
      key: "worker",
      action: "Worker offline",
      count: 1,
      subject: "",
      href: "/jobs",
      icon: ServerOff,
    });
  }

  return items;
}

// ── Activity ───────────────────────────────────────────────────────────────

/** One local calendar day of the viewer's activity. */
export interface ActivityDay {
  key: string;
  /** Narrow weekday label for the chart axis. */
  label: string;
  activeSeconds: number;
  opened: number;
  finished: number;
  isToday: boolean;
}

/** The viewer's last seven days, ending today, plus the most recent thing they opened. */
export interface WeekActivity {
  days: ActivityDay[];
  today: ActivityDay;
  latest: { title: string; kind: string; entityId: string; occurredAt: string } | null;
}

/** Reads the viewer's own consumption for the last seven local days. */
export async function loadWeekActivity(hideNsfw: boolean, signal?: AbortSignal): Promise<WeekActivity> {
  const now = new Date();
  const utcOffsetMinutes = localUtcOffsetMinutes(now);
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 6);
  const response = await fetchConsumptionStatistics(
    { from: start.toISOString(), to: now.toISOString(), hideNsfw, utcOffsetMinutes },
    { signal },
  );

  const buckets = new Map(response.dailyEvents.map((bucket) => [bucket.date, bucket]));
  const todayKey = localDayKey(now, utcOffsetMinutes);
  const weekday = new Intl.DateTimeFormat(undefined, { weekday: "narrow" });
  const days: ActivityDay[] = [];
  for (let offset = 0; offset < 7; offset += 1) {
    // Midday avoids a daylight-saving edge moving the instant onto a neighbouring calendar day.
    const day = new Date(start.getFullYear(), start.getMonth(), start.getDate() + offset, 12);
    const key = localDayKey(day, utcOffsetMinutes);
    const bucket = buckets.get(key);
    days.push({
      key,
      label: weekday.format(day),
      activeSeconds: statNumber(bucket?.activeSeconds),
      opened: statNumber(bucket?.accessedCount),
      finished: statNumber(bucket?.completedCount),
      isToday: key === todayKey,
    });
  }

  const recent = response.recentEvents[0];
  return {
    days,
    today: days[days.length - 1],
    latest: recent
      ? { title: recent.entityTitle, kind: recent.entityKind, entityId: recent.entityId, occurredAt: recent.occurredAt }
      : null,
  };
}
