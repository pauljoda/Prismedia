import { ACQUISITION_STATUS, THUMBNAIL_HOVER_KIND } from "$lib/api/generated/codes";
import {
  getCapability,
  getImagesCapability,
  getRatingValue,
  getTechnicalCapability,
  getThumbnailUrl,
  isNsfw,
  isWanted,
  type EntityCapabilityKind,
} from "$lib/api/capabilities";
import { numberValue, formatDurationString, durationToSeconds, normalized, formatResolutionLabel } from "$lib/utils/format";
import type { EntityCard, EntityCapability, ListEntitiesParams } from "$lib/api/generated/model";
import type { EntityThumbnail } from "$lib/api/generated/model";
import { isDeletableMediaKind } from "$lib/api/entity-deletion";
import {
  CAPABILITY_KIND,
  ENTITY_FILE_ROLE,
  ENTITY_KIND,
  labelForEntityKind,
} from "./entity-codes";
import {
  aspectRatioForKind,
  iconForKind,
  thumbnailToEntityShell,
  type EntityThumbnailAsset,
  type EntityThumbnailCard,
  type EntityThumbnailMetaIcon,
} from "./entity-thumbnail";

/**
 * Sentinel kind used when an entity grid is showing every returned entity kind
 * instead of narrowing to one tab.
 */
export const ENTITY_GRID_ALL_KINDS = "all";

export type EntityGridSort = "title" | "kind" | "rating" | "position" | "added" | "random" | "references";
export type EntityGridSortDir = "asc" | "desc";
export type EntityGridViewMode = "grid" | "list" | "feed";

export type EntityGridServerQuery = Pick<
  ListEntitiesParams,
  | "sort"
  | "sortDir"
  | "seed"
  | "favorite"
  | "organized"
  | "ratingMin"
  | "ratingMax"
  | "unrated"
  | "status"
  | "bookType"
  | "bookFormat"
  | "nsfw"
  | "hasFile"
  | "wanted"
  | "acquisitionStatus"
  | "played"
  | "orphaned"
>;

export interface EntityGridKindTab {
  kind: string;
  label: string;
  count: number;
}

export interface EntityGridFilterOption {
  id: string;
  count: number;
  label: string;
  capabilityKind: EntityCapabilityKind;
  value?: string;
}

/**
 * Page-level action shown when one or more EntityGrid cards are selected.
 * Routes can provide contextual actions such as merge, add to collection, or
 * queue processing without changing the shared grid component.
 */
export interface EntityGridBulkAction {
  id: string;
  label: string;
  tone?: "default" | "danger";
  /** Keeps an action out of the menu when the current selection cannot safely use it. */
  isAvailable?: (selectedIds: string[]) => boolean;
  onRun: (selectedIds: string[]) => void;
}

export interface EntityGridState {
  activeKind: string;
  filterIds: string[];
  includeNsfw: boolean;
  query: string;
  sortBy: EntityGridSort;
  sortDir: EntityGridSortDir;
  /** Seed for the random sort; reshuffled whenever the user picks Random again. */
  randomSeed: number;
}

export interface EntityGridRequest {
  filters: EntityGridFilterOption[];
  kind?: string;
  query?: string;
  sortBy: EntityGridSort;
  sortDir: EntityGridSortDir;
  /** Server-resolvable sort and filter parameters for the list endpoint. */
  server: EntityGridServerQuery;
}

export interface ApplyEntityGridStateOptions {
  /**
   * When true, server-owned sorts are treated as already ordered by the incoming
   * card sequence. Disable this for detail-page grids that only have local cards.
   */
  preserveServerResolvedSorts?: boolean;
  /** True when the host re-fetches server-resolved filters; false for local detail-page grids. */
  serverResolvedFilters?: boolean;
}

const AVAILABILITY_PREFIX = "availability:";

/** Mutually-exclusive library/acquisition states exposed by the shared EntityGrid drawer. */
export const AVAILABILITY_FILTER_DEFS = [
  { id: `${AVAILABILITY_PREFIX}on-disk`, label: "On disk", capabilityKind: CAPABILITY_KIND.files, value: "on-disk" },
  { id: `${AVAILABILITY_PREFIX}wanted`, label: "Wanted", capabilityKind: CAPABILITY_KIND.flags, value: "wanted" },
  { id: `${AVAILABILITY_PREFIX}${ACQUISITION_STATUS.pending}`, label: "Pending", capabilityKind: CAPABILITY_KIND.flags, value: ACQUISITION_STATUS.pending },
  { id: `${AVAILABILITY_PREFIX}${ACQUISITION_STATUS.searching}`, label: "Searching", capabilityKind: CAPABILITY_KIND.flags, value: ACQUISITION_STATUS.searching },
  { id: `${AVAILABILITY_PREFIX}${ACQUISITION_STATUS.awaitingSelection}`, label: "Review", capabilityKind: CAPABILITY_KIND.flags, value: ACQUISITION_STATUS.awaitingSelection },
  { id: `${AVAILABILITY_PREFIX}${ACQUISITION_STATUS.queued}`, label: "Queued", capabilityKind: CAPABILITY_KIND.flags, value: ACQUISITION_STATUS.queued },
  { id: `${AVAILABILITY_PREFIX}${ACQUISITION_STATUS.downloading}`, label: "Downloading", capabilityKind: CAPABILITY_KIND.flags, value: ACQUISITION_STATUS.downloading },
  { id: `${AVAILABILITY_PREFIX}${ACQUISITION_STATUS.downloaded}`, label: "Downloaded", capabilityKind: CAPABILITY_KIND.flags, value: ACQUISITION_STATUS.downloaded },
  { id: `${AVAILABILITY_PREFIX}${ACQUISITION_STATUS.importing}`, label: "Importing", capabilityKind: CAPABILITY_KIND.flags, value: ACQUISITION_STATUS.importing },
  { id: `${AVAILABILITY_PREFIX}${ACQUISITION_STATUS.imported}`, label: "Imported", capabilityKind: CAPABILITY_KIND.flags, value: ACQUISITION_STATUS.imported },
  { id: `${AVAILABILITY_PREFIX}${ACQUISITION_STATUS.stopping}`, label: "Cleaning up", capabilityKind: CAPABILITY_KIND.flags, value: ACQUISITION_STATUS.stopping },
  { id: `${AVAILABILITY_PREFIX}${ACQUISITION_STATUS.failed}`, label: "Failed", capabilityKind: CAPABILITY_KIND.flags, value: ACQUISITION_STATUS.failed },
  { id: `${AVAILABILITY_PREFIX}${ACQUISITION_STATUS.cancelled}`, label: "Cancelled", capabilityKind: CAPABILITY_KIND.flags, value: ACQUISITION_STATUS.cancelled },
  { id: `${AVAILABILITY_PREFIX}${ACQUISITION_STATUS.manualImportRequired}`, label: "Needs attention", capabilityKind: CAPABILITY_KIND.flags, value: ACQUISITION_STATUS.manualImportRequired },
] as const satisfies readonly Omit<EntityGridFilterOption, "count">[];

const AVAILABILITY_BY_ID: ReadonlyMap<string, Omit<EntityGridFilterOption, "count">> =
  new Map(AVAILABILITY_FILTER_DEFS.map((definition) => [definition.id, definition]));

/** Migrates the retired File chips while preserving persisted grid and preset intent. */
export function normalizeEntityGridFilterIds(ids: string[]): string[] {
  return [...new Set(ids.map((id) => {
    if (id === "files:has:true") return `${AVAILABILITY_PREFIX}on-disk`;
    if (id === "files:has:false") return `${AVAILABILITY_PREFIX}wanted`;
    return id;
  }))];
}

/**
 * Formats a backend entity kind code into the compact plural label shown in
 * EntityGrid tabs and lab surfaces.
 */
export function getEntityKindLabel(kind: string): string {
  return labelForEntityKind(kind);
}


function statLabel(code: string, value: number | string): string {
  const count = String(value);
  const label = code.replaceAll("-", " ");
  return `${count} ${label}`;
}

function statIcon(code: string): EntityThumbnailMetaIcon {
  if (code.includes("track")) return "audio";
  if (code.includes("page")) return "book";
  if (code.includes("chapter")) return "chapter";
  if (code.includes("image")) return "gallery";
  if (code.includes("credit")) return "person";
  return "count";
}

function isBitrateLabel(label: string): boolean {
  return /\b(?:[kmgt]bps|bps)\b/i.test(label);
}


function formatResolutionLabelFull(width: number, height: number): string {
  return formatResolutionLabel(height) ?? `${width}×${height}`;
}

type EntityGridSourceEntity = EntityCard | EntityThumbnail;
type EntityThumbnailWithAcquisitionStatuses = EntityThumbnail & {
  acquisitionStatuses?: string[] | null;
};

function isFullEntityCard(entity: EntityGridSourceEntity): entity is EntityCard {
  return "capabilities" in entity;
}

function capabilitiesForEntity(entity: EntityGridSourceEntity): EntityCapability[] {
  if (isFullEntityCard(entity)) return entity.capabilities;

  const caps: EntityCapability[] = [];
  if (entity.isFavorite || entity.isNsfw || entity.isOrganized || entity.isWanted) {
    caps.push({
      kind: "flags" as const,
      isFavorite: entity.isFavorite || null,
      isNsfw: entity.isNsfw || null,
      isOrganized: entity.isOrganized || null,
      isWanted: entity.isWanted || null,
    });
  }
  if (entity.rating != null) {
    caps.push({
      kind: "rating" as const,
      value: typeof entity.rating === "string" ? Number(entity.rating) : entity.rating,
    });
  }
  return caps;
}

function aspectRatioForEntity(entity: EntityGridSourceEntity): EntityThumbnailCard["aspectRatio"] {
  const technical = getTechnicalCapability(capabilitiesForEntity(entity));
  const width = numberValue(technical?.width);
  const height = numberValue(technical?.height);

  if ((entity.kind === ENTITY_KIND.image || entity.kind === ENTITY_KIND.video) && width && height) {
    return { width, height };
  }
  return aspectRatioForKind(entity.kind);
}

function assetFromPath(path: string, title: string, role?: string, entityId?: string): EntityThumbnailAsset {
  return {
    alt: role ? `${title} ${role}` : title,
    role,
    src: path,
    entityId,
  };
}

function sampleSpread<T>(items: T[], max: number): T[] {
  if (items.length <= max) return items;
  const result: T[] = [];
  const last = items.length - 1;
  for (let i = 0; i < max; i++) {
    result.push(items[Math.round((i * last) / (max - 1))]);
  }
  return result;
}

function thumbnailPreviewAsset(entity: EntityThumbnail): EntityThumbnailAsset | null {
  const path = entity.coverUrl ?? entity.hoverImages?.[0]?.path ?? null;
  return path ? assetFromPath(path, entity.title, "preview", entity.id) : null;
}

function childPreviewAssets(entity: EntityGridSourceEntity): EntityThumbnailAsset[] {
  if (!isFullEntityCard(entity)) return [];
  const childThumbnails = entity.childrenByKind.flatMap((group) => group.entities);
  return sampleSpread(childThumbnails, 5)
    .map(thumbnailPreviewAsset)
    .filter((asset): asset is EntityThumbnailAsset => Boolean(asset));
}

function previewAssets(entity: EntityGridSourceEntity, roles: string[]): EntityThumbnailAsset[] {
  if (!isFullEntityCard(entity) && entity.hoverImages?.length > 0) {
    return entity.hoverImages.map((image) => assetFromPath(image.path, image.title, "preview", image.entityId));
  }

  const images = getImagesCapability(capabilitiesForEntity(entity));
  if (!images) return [];

  const results: EntityThumbnailAsset[] = [];
  for (const item of images.items) {
    const role = String(item.kind);
    if (!roles.includes(role)) continue;
    if (role === ENTITY_FILE_ROLE.trickplay) continue;
    results.push(assetFromPath(item.path, entity.title, role));
  }
  return results;
}

/** Finds a VTT sprite map or Jellyfin image playlist from entity image assets. */
function findSpriteHover(entity: EntityGridSourceEntity): { spriteUrl?: string; vttUrl: string } | null {
  if (!isFullEntityCard(entity) && entity.hoverKind === THUMBNAIL_HOVER_KIND.sprite && entity.hoverUrl) {
    return { vttUrl: entity.hoverUrl };
  }

  const images = getImagesCapability(capabilitiesForEntity(entity));
  if (!images) return null;

  const playlistItem = images.items.find((item) => item.kind === ENTITY_FILE_ROLE.trickplay && item.path.endsWith(".m3u8"));
  if (playlistItem) {
    return { vttUrl: playlistItem.path };
  }

  const vttItem = images.items.find((item) => item.kind === ENTITY_FILE_ROLE.trickplay && item.path.endsWith(".vtt"));
  if (!vttItem) return null;

  const spriteItem = images.items.find((item) => item.kind === ENTITY_FILE_ROLE.sprite);
  const spriteUrl = spriteItem?.path ?? vttItem.path.replace(/\/[^/]+\.vtt$/, "/sprite");

  return { spriteUrl, vttUrl: vttItem.path };
}

function metaForEntity(entity: EntityGridSourceEntity): EntityThumbnailCard["meta"] {
  if (!isFullEntityCard(entity)) {
    return entity.meta
      .filter((item) => !isBitrateLabel(item.label))
      .map((item) => ({
        icon: (item.icon as EntityThumbnailMetaIcon) ?? iconForKind(entity.kind),
        label: item.label,
      }))
      .slice(0, 5);
  }

  const meta: EntityThumbnailCard["meta"] = [];
  const technical = getTechnicalCapability(entity.capabilities);
  const duration = formatDurationString(technical?.duration, false);
  const width = numberValue(technical?.width);
  const height = numberValue(technical?.height);
  const stats = getCapability(entity.capabilities, CAPABILITY_KIND.stats)?.items ?? [];
  const positions = getCapability(entity.capabilities, CAPABILITY_KIND.position)?.items ?? [];
  const customOverlay = customOverlayForEntity(entity);

  if (duration) meta.push({ icon: "duration", label: duration });
  if (width && height) meta.push({ icon: entity.kind === ENTITY_KIND.video ? "video" : "image", label: formatResolutionLabelFull(width, height) });
  if (entity.kind === ENTITY_KIND.video && technical?.codec) {
    meta.push({ icon: "video", label: technical.codec.toUpperCase() });
  }
  if (entity.kind === ENTITY_KIND.video && technical?.container) {
    meta.push({ icon: "video", label: technical.container.toUpperCase() });
  }
  // Stat codes are an open provider vocabulary, so this filters wire strings rather
  // than a closed code set. prism-vocab: external
  for (const stat of stats.filter((s) => !s.code.includes("bit-rate") && !s.code.includes("bitrate")).slice(0, 2)) {
    meta.push({ icon: statIcon(stat.code), label: statLabel(stat.code, stat.value) });
  }
  if (!customOverlay?.bottomLeft) {
    for (const position of positions.slice(0, 1)) {
      meta.push({ icon: iconForKind(entity.kind), label: position.label ?? `${position.code} ${position.value}` });
    }
  }

  return meta.slice(0, 5);
}

function positionValue(entity: EntityGridSourceEntity, code: string): number | null {
  const value = getCapability(capabilitiesForEntity(entity), CAPABILITY_KIND.position)?.items.find((item) => item.code === code)?.value;
  return numberValue(value);
}

function primaryPositionValue(entity: EntityGridSourceEntity): number | null {
  return positionValue(entity, "episode") ??
    positionValue(entity, "absolute-episode") ??
    positionValue(entity, "season") ??
    positionValue(entity, "sort") ??
    positionValue(entity, "chapter") ??
    positionValue(entity, "volume") ??
    numberValue(entity.sortOrder);
}

function customOverlayForEntity(entity: EntityGridSourceEntity): EntityThumbnailCard["custom"] {
  const season = positionValue(entity, "season");
  const episode = positionValue(entity, "episode") ?? positionValue(entity, "absolute-episode");

  if (entity.kind === ENTITY_KIND.video && season && episode) {
    return {
      bottomLeft: {
        label: `S${season} E${episode}`,
        title: `Season ${season}, Episode ${episode}`,
      },
    };
  }

  if (entity.kind === ENTITY_KIND.video && episode) {
    return {
      bottomLeft: {
        label: `E${episode}`,
        title: `Episode ${episode}`,
      },
    };
  }

  if (entity.kind === ENTITY_KIND.videoSeason && season) {
    return {
      bottomLeft: {
        label: `S${season}`,
        title: `Season ${season}`,
      },
    };
  }

  return undefined;
}

/**
 * Resolves the 0..1 progress meter fraction for a thumbnail. Lightweight browse rows carry a
 * precomputed `progress` field; full entity cards derive it from the shared playback capability
 * (videos: completed → 1, else resume position over known runtime) or progress capability
 * (books: completed → 1, else current index over total). Returns null when there is nothing to show.
 */
function progressForEntity(entity: EntityGridSourceEntity): number | null {
  const clamp01 = (value: number): number => Math.min(1, Math.max(0, value));

  if (!isFullEntityCard(entity)) {
    const value = numberValue(entity.progress);
    return value != null && Number.isFinite(value) ? clamp01(value) : null;
  }

  const capabilities = entity.capabilities;
  const playback = getCapability(capabilities, CAPABILITY_KIND.playback);
  if (playback) {
    if (playback.completedAt) return 1;
    const resumeSeconds = numberValue(playback.resumeSeconds) ?? 0;
    const durationSeconds = durationToSeconds(getTechnicalCapability(capabilities)?.duration ?? null) ?? 0;
    return resumeSeconds > 0 && durationSeconds > 0 ? clamp01(resumeSeconds / durationSeconds) : null;
  }

  const progress = getCapability(capabilities, CAPABILITY_KIND.progress);
  if (progress) {
    if (progress.completedAt) return 1;
    const total = numberValue(progress.total) ?? 0;
    const index = numberValue(progress.index) ?? 0;
    return total > 0 && index > 0 ? clamp01(index / total) : null;
  }

  return null;
}

/**
 * Converts a generated entity card into the shared thumbnail card model.
 * The mapper reads only shared capabilities so every entity kind can flow
 * through one thumbnail component.
 */
export function entityCardToThumbnailCard(
  entity: EntityGridSourceEntity,
  href?: string,
): EntityThumbnailCard {
  const capabilities = capabilitiesForEntity(entity);
  const images = getImagesCapability(capabilities);
  // Use only explicit thumbnail/cover URLs from the backend. The items[0]
  // fallback is intentionally restricted to cover/poster/thumbnail roles —
  // generated assets like trickplay VTTs or previews should never be used as
  // a static cover image, and source files are not displayable thumbnails.
  const coverPath =
    (!isFullEntityCard(entity) ? entity.coverUrl : null) ??
    images?.coverUrl ??
    getThumbnailUrl(capabilities) ??
    images?.items.find(
      (item) =>
        item.kind === ENTITY_FILE_ROLE.cover ||
        item.kind === ENTITY_FILE_ROLE.poster ||
        item.kind === ENTITY_FILE_ROLE.thumbnail ||
        item.kind === ENTITY_FILE_ROLE.logo,
    )?.path ??
    null;

  // Small grid variants paired with the cover, used for the responsive srcset.
  // Full cards carry them on the images capability; list thumbnails inline them.
  // The capability's thumbnailUrl falls back to the full cover when no grid
  // variant exists yet, so an echo of the cover is treated as "no small variant".
  const coverThumbCandidate = isFullEntityCard(entity)
    ? images?.thumbnailUrl ?? null
    : entity.coverThumbUrl ?? null;
  const coverThumbPath = coverThumbCandidate === coverPath ? null : coverThumbCandidate;
  const coverThumb2xPath = isFullEntityCard(entity)
    ? images?.thumbnail2xUrl ?? null
    : entity.coverThumb2xUrl ?? null;

  // When the cover stands in for a distinct child entity (a gallery's
  // representative cover image), keep that child's id on the cover asset so the
  // feed can hydrate and autoplay the underlying media rather than the container.
  const coverChildEntityId = !isFullEntityCard(entity)
    ? entity.hoverImages?.find((image) => image.path === coverPath)?.entityId
    : undefined;

  const spriteHover = findSpriteHover(entity);
  const imageSequence = previewAssets(entity, [ENTITY_FILE_ROLE.trickplay, ENTITY_FILE_ROLE.sprite]);
  const childSequence = imageSequence.length > 0 ? [] : childPreviewAssets(entity);
  const latestAcquisitionStatus = isFullEntityCard(entity)
    ? null
    : entity.latestAcquisitionStatus ?? null;
  const projectedAcquisitionStatuses = isFullEntityCard(entity)
    ? []
    : (entity as EntityThumbnailWithAcquisitionStatuses).acquisitionStatuses ?? [];
  const acquisitionStatuses = projectedAcquisitionStatuses.length > 0
    ? [...new Set(projectedAcquisitionStatuses)]
    : latestAcquisitionStatus
      ? [latestAcquisitionStatus]
      : [];
  const hover: EntityThumbnailCard["hover"] = spriteHover
    ? { kind: THUMBNAIL_HOVER_KIND.sprite, spriteUrl: spriteHover.spriteUrl, vttUrl: spriteHover.vttUrl }
    : imageSequence.length > 0
      ? { kind: THUMBNAIL_HOVER_KIND.imageSequence, assets: imageSequence }
      : childSequence.length > 0
        ? { kind: THUMBNAIL_HOVER_KIND.imageSequence, assets: childSequence }
      : { kind: THUMBNAIL_HOVER_KIND.none };

  return {
    aspectRatio: aspectRatioForEntity(entity),
    cover: coverPath
      ? {
          ...assetFromPath(coverPath, entity.title, ENTITY_FILE_ROLE.cover, coverChildEntityId),
          thumbSrc: coverThumbPath ?? undefined,
          thumbSrc2x: coverThumbPath ? coverThumb2xPath ?? undefined : undefined,
        }
      : null,
    custom: customOverlayForEntity(entity),
    entity: {
      ...(isFullEntityCard(entity) ? entity : thumbnailToEntityShell(entity)),
      capabilities,
    },
    fit: "cover",
    hover,
    href,
    meta: metaForEntity(entity),
    progress: progressForEntity(entity),
    hasSourceMedia: Boolean(entity.hasSourceMedia),
    // Only the thumbnail read model carries the wanted acquisition status; detail cards don't.
    wantedStatus: isFullEntityCard(entity) ? null : entity.wantedStatus ?? null,
    latestAcquisitionStatus,
    acquisitionStatuses,
  };
}

/** Every subtree status available for filtering, with compatibility fallback for older API rows. */
function acquisitionStatusesForCard(card: EntityThumbnailCard): string[] {
  const statuses = card.acquisitionStatuses?.filter((status) => status.length > 0) ?? [];
  if (statuses.length > 0) return [...new Set(statuses)];
  return card.latestAcquisitionStatus ? [card.latestAcquisitionStatus] : [];
}

/**
 * Builds the kind tab list from the entities returned by the current request.
 * Counts are intentionally local to the returned collection so mixed surfaces
 * can render their own scoped tabs.
 */
export function buildEntityKindTabs(
  cards: EntityThumbnailCard[],
  options: { includeNsfw?: boolean } = {},
): EntityGridKindTab[] {
  const counts = new Map<string, number>();
  for (const card of cards) {
    if (options.includeNsfw === false && isNsfw(card.entity.capabilities)) continue;
    counts.set(card.entity.kind, (counts.get(card.entity.kind) ?? 0) + 1);
  }

  return [...counts.entries()]
    .map(([kind, count]) => ({ kind, label: getEntityKindLabel(kind), count }))
    .sort((left, right) => left.label.localeCompare(right.label));
}

/**
 * Adaptive engagement-status filters. A single control reads correctly across
 * kinds: the backend resolves `watched`/`read` (completed), `unwatched`/`unread`
 * (no engagement), and `in-progress` against both the playback and progress
 * capabilities, so the same filter value works for videos, audio, books, and
 * comics. The drawer swaps in kind-specific labels.
 */
export const STATUS_FILTER_DEFS = [
  { id: "status:watched", value: "watched", defaultLabel: "Watched" },
  { id: "status:unwatched", value: "unwatched", defaultLabel: "Unwatched" },
  { id: "status:in-progress", value: "in-progress", defaultLabel: "In progress" },
] as const;

const STATUS_VALUE_BY_ID = new Map<string, string>(STATUS_FILTER_DEFS.map((def) => [def.id, def.value]));

/**
 * Book type filter options (the {@link BookType} closed set). These are resolved
 * entirely server-side against the book detail row — list thumbnails do not carry
 * the type — so they are surfaced only on the Books grid and skipped by the
 * client-side pass in {@link applyEntityGridState}.
 */
export const BOOK_TYPE_FILTER_DEFS = [
  { id: "book-type:book", code: "book", label: "Book" },
  { id: "book-type:comic", code: "comic", label: "Comic" },
  { id: "book-type:manga", code: "manga", label: "Manga" },
  { id: "book-type:novel", code: "novel", label: "Novel" },
] as const;

/** Book format filter options (the {@link BookFormat} closed set). Server-resolved like the types. */
export const BOOK_FORMAT_FILTER_DEFS = [
  { id: "book-format:image-archive", code: "image-archive", label: "Comic Archive" },
  { id: "book-format:epub", code: "epub", label: "EPUB" },
  { id: "book-format:pdf", code: "pdf", label: "PDF" },
] as const;

const BOOK_TYPE_LABEL_BY_ID = new Map<string, { id: string; code: string; label: string }>(
  BOOK_TYPE_FILTER_DEFS.map((def) => [def.id, def]),
);
const BOOK_FORMAT_LABEL_BY_ID = new Map<string, { id: string; code: string; label: string }>(
  BOOK_FORMAT_FILTER_DEFS.map((def) => [def.id, def]),
);
const BOOK_TYPE_PREFIX = "book-type:";
const BOOK_FORMAT_PREFIX = "book-format:";

/**
 * Filter IDs whose effect is resolved by the list endpoint across the whole
 * matching set rather than by client-side card inspection. These are skipped by
 * {@link applyEntityGridState} so the loaded page is not re-filtered (often with
 * data the thumbnail does not carry, such as playback/progress) on top of the
 * server result.
 */
export function isServerResolvedFilterId(id: string): boolean {
  return (
    id === "flags:favorite" ||
    id === "flags:organized:true" ||
    id === "flags:organized:false" ||
    id === "flags:nsfw:true" ||
    id === "flags:nsfw:false" ||
    id === "files:has:true" ||
    id === "files:has:false" ||
    id.startsWith(AVAILABILITY_PREFIX) ||
    id === "progress:played:true" ||
    id === "progress:played:false" ||
    id === "taxonomy:orphaned" ||
    id === "taxonomy:referenced" ||
    id === "rating:unrated" ||
    id.startsWith("rating:min:") ||
    id.startsWith("rating:max:") ||
    id.startsWith("status:") ||
    id.startsWith(BOOK_TYPE_PREFIX) ||
    id.startsWith(BOOK_FORMAT_PREFIX)
  );
}

/**
 * Folds the active filter IDs into the server-resolvable query. Rating bounds
 * collapse to the tightest active min/max; favorite and organized resolve to
 * booleans; the engagement status takes the first selected value.
 */
export function buildServerQueryFromFilters(filterIds: string[]): EntityGridServerQuery {
  const server: EntityGridServerQuery = {};
  const bookTypes: string[] = [];
  const bookFormats: string[] = [];
  for (const id of filterIds) {
    if (id === "flags:favorite") {
      server.favorite = true;
    } else if (id === "flags:organized:true") {
      server.organized = true;
    } else if (id === "flags:organized:false") {
      server.organized = false;
    } else if (id === "flags:nsfw:true") {
      server.nsfw = true;
    } else if (id === "flags:nsfw:false") {
      server.nsfw = false;
    } else if (id === "files:has:true") {
      server.hasFile = true;
    } else if (id === "files:has:false") {
      server.hasFile = false;
    } else if (id === `${AVAILABILITY_PREFIX}on-disk`) {
      server.hasFile = true;
    } else if (id === `${AVAILABILITY_PREFIX}wanted`) {
      server.wanted = true;
    } else if (AVAILABILITY_BY_ID.has(id)) {
      server.acquisitionStatus = AVAILABILITY_BY_ID.get(id)?.value;
    } else if (id === "progress:played:true") {
      server.played = true;
    } else if (id === "progress:played:false") {
      server.played = false;
    } else if (id === "taxonomy:orphaned") {
      server.orphaned = true;
    } else if (id === "taxonomy:referenced") {
      server.orphaned = false;
    } else if (id === "rating:unrated") {
      server.unrated = true;
    } else if (id.startsWith("rating:min:")) {
      const value = Number(id.slice("rating:min:".length));
      if (Number.isFinite(value)) server.ratingMin = Math.max(numberValue(server.ratingMin) ?? value, value);
    } else if (id.startsWith("rating:max:")) {
      const value = Number(id.slice("rating:max:".length));
      if (Number.isFinite(value)) server.ratingMax = Math.min(numberValue(server.ratingMax) ?? value, value);
    } else if (STATUS_VALUE_BY_ID.has(id)) {
      server.status = STATUS_VALUE_BY_ID.get(id);
    } else if (id.startsWith(BOOK_TYPE_PREFIX)) {
      bookTypes.push(id.slice(BOOK_TYPE_PREFIX.length));
    } else if (id.startsWith(BOOK_FORMAT_PREFIX)) {
      bookFormats.push(id.slice(BOOK_FORMAT_PREFIX.length));
    }
  }
  // The book families OR within themselves (any selected type/format matches) and
  // the server ANDs the two families together.
  if (bookTypes.length > 0) server.bookType = bookTypes.join(",");
  if (bookFormats.length > 0) server.bookFormat = bookFormats.join(",");
  return server;
}

function addOption(options: Map<string, EntityGridFilterOption>, option: Omit<EntityGridFilterOption, "count">) {
  const existing = options.get(option.id);
  if (existing) {
    options.set(option.id, { ...existing, count: existing.count + 1 });
  } else {
    options.set(option.id, { ...option, count: 1 });
  }
}

function addUniqueOption(options: Map<string, EntityGridFilterOption>, option: Omit<EntityGridFilterOption, "count">) {
  if (!options.has(option.id)) options.set(option.id, { ...option, count: 0 });
}

/**
 * Builds selectable filter chips from the capabilities actually present on the
 * returned cards. Counts reflect entities that would match the filter, not just
 * entities that support the capability family.
 */
export function buildCapabilityFilterOptions(
  cards: EntityThumbnailCard[],
  kind?: string,
): EntityGridFilterOption[] {
  const options = new Map<string, EntityGridFilterOption>();
  const hasDates = true;
  const hasAvailability = kind == null
    ? cards.some((card) => card.hasSourceMedia || isWanted(card.entity.capabilities) || acquisitionStatusesForCard(card).length > 0)
    : isDeletableMediaKind(kind);
  const hasFlags = cards.some((card) => Boolean(getCapability(card.entity.capabilities, CAPABILITY_KIND.flags)));
  const hasProgress = true;
  const hasRating = cards.some((card) => getRatingValue(card.entity.capabilities) > 0);
  const hasTechnical = cards.some((card) => Boolean(getTechnicalCapability(card.entity.capabilities)));

  // Taxonomy kinds can be filtered to the orphaned/empty entries nothing references — resolved
  // server-side so it spans the whole library, not just the loaded page.
  if (kind === ENTITY_KIND.tag || kind === ENTITY_KIND.person || kind === ENTITY_KIND.studio) {
    addUniqueOption(options, {
      id: "taxonomy:orphaned",
      label: "Orphaned",
      capabilityKind: CAPABILITY_KIND.flags,
      value: "orphaned",
    });
  }

  if (hasRating) {
    for (const value of [1, 2, 3, 4, 5]) {
      addUniqueOption(options, { id: `rating:min:${value}`, label: `${value}★+`, capabilityKind: CAPABILITY_KIND.rating, value: `min:${value}` });
      addUniqueOption(options, { id: `rating:max:${value}`, label: `≤${value}★`, capabilityKind: CAPABILITY_KIND.rating, value: `max:${value}` });
    }
  }

  if (hasTechnical) {
    for (const resolution of ["4K", "1080p", "720p", "480p"]) {
      addUniqueOption(options, {
        id: `technical:resolution:${resolution}`,
        label: resolution,
        capabilityKind: CAPABILITY_KIND.technical,
        value: `resolution:${resolution}`,
      });
    }
    for (const duration of ["lt300", "300-900", "900-1800", "gte1800"]) {
      addUniqueOption(options, {
        id: `technical:duration:${duration}`,
        label: duration,
        capabilityKind: CAPABILITY_KIND.technical,
        value: `duration:${duration}`,
      });
    }
  }

  if (hasDates) {
    addUniqueOption(options, { id: "dates:from", label: "Date from", capabilityKind: CAPABILITY_KIND.dates, value: "from" });
    addUniqueOption(options, { id: "dates:to", label: "Date to", capabilityKind: CAPABILITY_KIND.dates, value: "to" });
  }

  if (hasAvailability) {
    for (const definition of AVAILABILITY_FILTER_DEFS) addUniqueOption(options, definition);
  }

  if (hasProgress) {
    addUniqueOption(options, { id: "progress:played:true", label: "Played", capabilityKind: CAPABILITY_KIND.progress, value: "played:true" });
    addUniqueOption(options, { id: "progress:played:false", label: "Unplayed", capabilityKind: CAPABILITY_KIND.progress, value: "played:false" });
  }

  if (hasFlags) {
    addUniqueOption(options, { id: "flags:favorite", label: "Favorites", capabilityKind: CAPABILITY_KIND.flags, value: "favorite" });
    addUniqueOption(options, { id: "flags:organized:true", label: "Organized", capabilityKind: CAPABILITY_KIND.flags, value: "organized:true" });
    addUniqueOption(options, { id: "flags:organized:false", label: "Not organized", capabilityKind: CAPABILITY_KIND.flags, value: "organized:false" });
    addUniqueOption(options, { id: "flags:nsfw:true", label: "Is NSFW", capabilityKind: CAPABILITY_KIND.flags, value: "nsfw:true" });
    addUniqueOption(options, { id: "flags:nsfw:false", label: "Not NSFW", capabilityKind: CAPABILITY_KIND.flags, value: "nsfw:false" });
  }

  // Rating bucket helpers that don't depend on a loaded card already carrying a
  // rating — "Unrated" is resolved entirely server-side.
  addUniqueOption(options, { id: "rating:unrated", label: "Unrated", capabilityKind: CAPABILITY_KIND.rating, value: "unrated" });

  // Adaptive engagement status. The server resolves these against playback
  // (videos/audio) and progress (books/comics), so they are offered on every
  // page; the drawer relabels them per kind.
  for (const status of STATUS_FILTER_DEFS) {
    addUniqueOption(options, {
      id: status.id,
      label: status.defaultLabel,
      capabilityKind: CAPABILITY_KIND.progress,
      value: status.value,
    });
  }

  for (const { entity } of cards) {
    for (const capability of entity.capabilities) {
      switch (capability.kind) {
        case CAPABILITY_KIND.flags:
          if (capability.isFavorite) {
            addOption(options, { id: "flags:favorite", label: "Favorites", capabilityKind: CAPABILITY_KIND.flags, value: "favorite" });
          }
          addOption(options, {
            id: `flags:organized:${capability.isOrganized ? "true" : "false"}`,
            label: capability.isOrganized ? "Organized" : "Not organized",
            capabilityKind: CAPABILITY_KIND.flags,
            value: `organized:${capability.isOrganized ? "true" : "false"}`,
          });
          addOption(options, {
            id: `flags:nsfw:${capability.isNsfw ? "true" : "false"}`,
            label: capability.isNsfw ? "Is NSFW" : "Not NSFW",
            capabilityKind: CAPABILITY_KIND.flags,
            value: `nsfw:${capability.isNsfw ? "true" : "false"}`,
          });
          if (capability.isOrganized) {
            addOption(options, { id: "flags:organized", label: "Organized", capabilityKind: CAPABILITY_KIND.flags, value: "organized" });
          }
          if (capability.isNsfw) {
            addOption(options, { id: "flags:nsfw", label: "NSFW", capabilityKind: CAPABILITY_KIND.flags, value: "nsfw" });
          }
          break;
        case CAPABILITY_KIND.rating: {
          const ratingValue = numberValue(capability.value);
          if (ratingValue && ratingValue > 0) {
            addOption(options, { id: "rating:any", label: "Rated", capabilityKind: CAPABILITY_KIND.rating });
            for (const value of [1, 2, 3, 4, 5]) {
              if (ratingValue >= value) {
                addOption(options, { id: `rating:min:${value}`, label: `${value}★+`, capabilityKind: CAPABILITY_KIND.rating, value: `min:${value}` });
              }
              if (ratingValue <= value) {
                addOption(options, { id: `rating:max:${value}`, label: `≤${value}★`, capabilityKind: CAPABILITY_KIND.rating, value: `max:${value}` });
              }
            }
          }
          if (ratingValue && ratingValue >= 4) {
            addOption(options, { id: "rating:4", label: "Rating 4+", capabilityKind: CAPABILITY_KIND.rating, value: "4" });
          }
          break;
        }
        case CAPABILITY_KIND.images:
          for (const role of new Set(capability.items.map((item) => String(item.kind)))) {
            addOption(options, {
              id: `images:${role}`,
              label: `Has ${role} image`,
              capabilityKind: CAPABILITY_KIND.images,
              value: role,
            });
          }
          break;
        case CAPABILITY_KIND.stats:
          for (const stat of capability.items) {
            addOption(options, {
              id: `stats:${stat.code}`,
              label: `Has ${stat.code.replaceAll("-", " ")}`,
              capabilityKind: CAPABILITY_KIND.stats,
              value: stat.code,
            });
          }
          break;
        case CAPABILITY_KIND.technical:
          if (capability.duration) {
            addOption(options, { id: "technical:duration", label: "Has duration", capabilityKind: CAPABILITY_KIND.technical, value: "duration" });
            const seconds = durationToSeconds(capability.duration);
            if (seconds != null) {
              if (seconds < 300) addOption(options, { id: "technical:duration:lt300", label: "< 5 min", capabilityKind: CAPABILITY_KIND.technical, value: "duration:lt300" });
              if (seconds >= 300 && seconds < 900) addOption(options, { id: "technical:duration:300-900", label: "5-15 min", capabilityKind: CAPABILITY_KIND.technical, value: "duration:300-900" });
              if (seconds >= 900 && seconds < 1800) addOption(options, { id: "technical:duration:900-1800", label: "15-30 min", capabilityKind: CAPABILITY_KIND.technical, value: "duration:900-1800" });
              if (seconds >= 1800) addOption(options, { id: "technical:duration:gte1800", label: "30+ min", capabilityKind: CAPABILITY_KIND.technical, value: "duration:gte1800" });
            }
          }
          if (capability.width && capability.height) addOption(options, { id: "technical:dimensions", label: "Has dimensions", capabilityKind: CAPABILITY_KIND.technical, value: "dimensions" });
          {
            const height = numberValue(capability.height);
            if (height) {
              if (height >= 2160) addOption(options, { id: "technical:resolution:4K", label: "4K", capabilityKind: CAPABILITY_KIND.technical, value: "resolution:4K" });
              if (height >= 1080 && height < 2160) addOption(options, { id: "technical:resolution:1080p", label: "1080p", capabilityKind: CAPABILITY_KIND.technical, value: "resolution:1080p" });
              if (height >= 720 && height < 1080) addOption(options, { id: "technical:resolution:720p", label: "720p", capabilityKind: CAPABILITY_KIND.technical, value: "resolution:720p" });
              if (height > 0 && height < 720) addOption(options, { id: "technical:resolution:480p", label: "480p", capabilityKind: CAPABILITY_KIND.technical, value: "resolution:480p" });
            }
          }
          if (capability.codec) addOption(options, { id: `technical:codec:${capability.codec}`, label: `Codec: ${capability.codec}`, capabilityKind: CAPABILITY_KIND.technical, value: `codec:${capability.codec}` });
          break;
        case CAPABILITY_KIND.dates:
          for (const date of capability.items) {
            addOption(options, {
              id: `dates:${date.code}`,
              label: `Has ${date.code.replaceAll("-", " ")} date`,
              capabilityKind: CAPABILITY_KIND.dates,
              value: date.code,
            });
          }
          break;
        case CAPABILITY_KIND.position:
          for (const position of capability.items) {
            addOption(options, {
              id: `position:${position.code}`,
              label: `Has ${position.code.replaceAll("-", " ")}`,
              capabilityKind: CAPABILITY_KIND.position,
              value: position.code,
            });
          }
          break;
        case CAPABILITY_KIND.classification:
          if (capability.value) {
            addOption(options, {
              id: `classification:${capability.value}`,
              label: `Classification: ${capability.value}`,
              capabilityKind: CAPABILITY_KIND.classification,
              value: capability.value,
            });
          }
          break;
        // File availability is projected once from source-backed Entity ownership below. FilesCapability
        // remains a detail payload, not a second Has file / No file filter vocabulary.
        case CAPABILITY_KIND.files:
          break;
        case CAPABILITY_KIND.progress:
          addOption(options, {
            id: `progress:played:${capability.completedAt ? "true" : "false"}`,
            label: capability.completedAt ? "Played" : "Unplayed",
            capabilityKind: CAPABILITY_KIND.progress,
            value: `played:${capability.completedAt ? "true" : "false"}`,
          });
          break;
      }
    }
  }

  for (const card of cards) {
    if (card.hasSourceMedia) addOption(options, AVAILABILITY_FILTER_DEFS[0]);
    if (isWanted(card.entity.capabilities)) addOption(options, AVAILABILITY_FILTER_DEFS[1]);
    for (const status of acquisitionStatusesForCard(card)) {
      const statusDefinition = AVAILABILITY_BY_ID.get(`${AVAILABILITY_PREFIX}${status}`);
      if (statusDefinition) addOption(options, statusDefinition);
    }
  }

  return [...options.values()].sort((left, right) => left.label.localeCompare(right.label));
}

/**
 * Resolves a persisted or dynamic filter ID into the full option metadata used
 * by chips, client-side lab filtering, and request serialization.
 */
export function entityGridFilterFromId(
  id: string,
  filterOptions: EntityGridFilterOption[],
): EntityGridFilterOption | undefined {
  const existing = filterOptions.find((option) => option.id === id);
  if (existing) return existing;

  const availability = AVAILABILITY_BY_ID.get(id);
  if (availability) return { ...availability, count: 0 };

  const [family, key, value] = id.split(":");
  if (family === CAPABILITY_KIND.dates && (key === "from" || key === "to") && value) {
    return {
      id,
      count: 0,
      label: key === "from" ? `Date from ${value}` : `Date to ${value}`,
      capabilityKind: CAPABILITY_KIND.dates,
      value: `${key}:${value}`,
    };
  }

  // Server-resolved filters must resolve even before any cards (and therefore
  // any capability-derived options) have loaded, so chips and snapshots survive.
  if (id === "flags:favorite") {
    return { id, count: 0, label: "Favorites", capabilityKind: CAPABILITY_KIND.flags, value: "favorite" };
  }
  if (id === "rating:unrated") {
    return { id, count: 0, label: "Unrated", capabilityKind: CAPABILITY_KIND.rating, value: "unrated" };
  }
  if (id.startsWith("rating:min:") && value) {
    return { id, count: 0, label: `${value}★+`, capabilityKind: CAPABILITY_KIND.rating, value: `min:${value}` };
  }
  if (id.startsWith("rating:max:") && value) {
    return { id, count: 0, label: `≤${value}★`, capabilityKind: CAPABILITY_KIND.rating, value: `max:${value}` };
  }
  const statusDef = STATUS_FILTER_DEFS.find((def) => def.id === id);
  if (statusDef) {
    return { id, count: 0, label: statusDef.defaultLabel, capabilityKind: CAPABILITY_KIND.progress, value: statusDef.value };
  }
  const bookTypeDef = BOOK_TYPE_LABEL_BY_ID.get(id);
  if (bookTypeDef) {
    return { id, count: 0, label: bookTypeDef.label, capabilityKind: CAPABILITY_KIND.classification, value: bookTypeDef.code };
  }
  const bookFormatDef = BOOK_FORMAT_LABEL_BY_ID.get(id);
  if (bookFormatDef) {
    return { id, count: 0, label: bookFormatDef.label, capabilityKind: CAPABILITY_KIND.classification, value: bookFormatDef.code };
  }
  return undefined;
}

function entityMatchesFilter(capabilities: EntityCapability[], filter: EntityGridFilterOption): boolean {
  switch (filter.capabilityKind) {
    case CAPABILITY_KIND.flags: {
      const flags = getCapability(capabilities, CAPABILITY_KIND.flags);
      if (filter.value === "favorite") return flags?.isFavorite === true;
      if (filter.value === "organized:true") return flags?.isOrganized === true;
      if (filter.value === "organized:false") return flags?.isOrganized === false;
      if (filter.value === "nsfw:true") return flags?.isNsfw === true;
      if (filter.value === "nsfw:false") return flags?.isNsfw === false;
      if (filter.value === "organized") return flags?.isOrganized === true;
      if (filter.value === "nsfw") return flags?.isNsfw === true;
      return Boolean(flags);
    }
    case CAPABILITY_KIND.rating: {
      const value = getRatingValue(capabilities);
      if (filter.value?.startsWith("min:")) return value >= Number(filter.value.slice("min:".length));
      if (filter.value?.startsWith("max:")) return value <= Number(filter.value.slice("max:".length));
      return filter.value ? value >= Number(filter.value) : value > 0;
    }
    case CAPABILITY_KIND.images: {
      const images = getImagesCapability(capabilities);
      return Boolean(images && (!filter.value || images.items.some((item) => item.kind === filter.value)));
    }
    case CAPABILITY_KIND.stats:
      return getCapability(capabilities, CAPABILITY_KIND.stats)?.items.some((item) => item.code === filter.value) === true;
    case CAPABILITY_KIND.technical: {
      const technical = getTechnicalCapability(capabilities);
      if (!technical) return false;
      if (filter.value === "duration") return Boolean(technical.duration);
      if (filter.value?.startsWith("duration:")) {
        const seconds = durationToSeconds(technical.duration);
        if (seconds == null) return false;
        const bucket = filter.value.slice("duration:".length);
        if (bucket === "lt300") return seconds < 300;
        if (bucket === "300-900") return seconds >= 300 && seconds < 900;
        if (bucket === "900-1800") return seconds >= 900 && seconds < 1800;
        if (bucket === "gte1800") return seconds >= 1800;
      }
      if (filter.value === "dimensions") return Boolean(technical.width && technical.height);
      if (filter.value?.startsWith("resolution:")) {
        const height = numberValue(technical.height);
        if (!height) return false;
        const resolution = filter.value.slice("resolution:".length);
        if (resolution === "4K") return height >= 2160;
        if (resolution === "1080p") return height >= 1080 && height < 2160;
        if (resolution === "720p") return height >= 720 && height < 1080;
        if (resolution === "480p") return height > 0 && height < 720;
      }
      if (filter.value?.startsWith("codec:")) return normalized(technical.codec) === normalized(filter.value.slice("codec:".length));
      return true;
    }
    case CAPABILITY_KIND.dates:
      if (filter.value?.startsWith("from:") || filter.value?.startsWith("to:")) {
        const [direction, date] = filter.value.split(":");
        const values = getCapability(capabilities, CAPABILITY_KIND.dates)?.items.map((item) => item.sortableValue ?? item.value) ?? [];
        return values.some((candidate) => direction === "from" ? candidate >= date : candidate <= date);
      }
      return getCapability(capabilities, CAPABILITY_KIND.dates)?.items.some((item) => item.code === filter.value) === true;
    case CAPABILITY_KIND.files: {
      const hasFiles = (getCapability(capabilities, CAPABILITY_KIND.files)?.items.length ?? 0) > 0;
      if (filter.value === "has:true") return hasFiles;
      if (filter.value === "has:false") return !hasFiles;
      return hasFiles;
    }
    case CAPABILITY_KIND.progress: {
      const progress = getCapability(capabilities, CAPABILITY_KIND.progress);
      if (filter.value === "played:true") return Boolean(progress?.completedAt);
      if (filter.value === "played:false") return !progress?.completedAt;
      return Boolean(progress);
    }
    case CAPABILITY_KIND.position:
      return getCapability(capabilities, CAPABILITY_KIND.position)?.items.some((item) => item.code === filter.value) === true;
    case CAPABILITY_KIND.classification:
      return getCapability(capabilities, CAPABILITY_KIND.classification)?.value === filter.value;
    default:
      return capabilities.some((capability) => capability.kind === filter.capabilityKind);
  }
}

function seededRandom(seed: number): () => number {
  let state = seed >>> 0;
  return () => {
    state += 0x6D2B79F5;
    let value = state;
    value = Math.imul(value ^ (value >>> 15), value | 1);
    value ^= value + Math.imul(value ^ (value >>> 7), value | 61);
    return ((value ^ (value >>> 14)) >>> 0) / 4294967296;
  };
}

function seededShuffle<T>(items: T[], seed: number): T[] {
  const result = [...items];
  const random = seededRandom(seed || 1);
  for (let i = result.length - 1; i > 0; i--) {
    const j = Math.floor(random() * (i + 1));
    [result[i], result[j]] = [result[j], result[i]];
  }
  return result;
}

/**
 * Applies the client-side version of EntityGrid state for lab and optimistic UI
 * paths. Server-backed endpoints own privacy filtering from server-side
 * settings, so this helper is only a local display fallback for cards the
 * client already has.
 */
export function applyEntityGridState(
  cards: EntityThumbnailCard[],
  state: EntityGridState,
  filterOptions = buildCapabilityFilterOptions(cards),
  options: ApplyEntityGridStateOptions = {},
): EntityThumbnailCard[] {
  const query = state.query.trim().toLowerCase();
  // Server-resolved filters (favorite, organized, rating bounds, unrated, and
  // engagement status) have already pruned the result set on the server, and the
  // list thumbnail does not even carry the data some of them need. Applying them
  // again here would wrongly empty the page, so they are excluded from the local
  // pass and only the genuinely client-side filters are re-checked.
  const serverResolvedFilters = options.serverResolvedFilters ?? true;
  const filters = state.filterIds
    .filter((id) => !serverResolvedFilters || !isServerResolvedFilterId(id))
    .map((id) => entityGridFilterFromId(id, filterOptions))
    .filter((option): option is EntityGridFilterOption => Boolean(option));

  const filtered = cards.filter((card) => {
    if (!state.includeNsfw && isNsfw(card.entity.capabilities)) return false;
    if (state.activeKind !== ENTITY_GRID_ALL_KINDS && card.entity.kind !== state.activeKind) return false;
    if (query && !card.entity.title.toLowerCase().includes(query)) return false;
    return filters.every((filter) => {
      if (filter.id === `${AVAILABILITY_PREFIX}on-disk`) return card.hasSourceMedia;
      if (filter.id === `${AVAILABILITY_PREFIX}wanted`) return isWanted(card.entity.capabilities);
      if (filter.id.startsWith(AVAILABILITY_PREFIX)) {
        return acquisitionStatusesForCard(card).includes(filter.value ?? "");
      }
      return entityMatchesFilter(card.entity.capabilities, filter);
    });
  });

  const preserveServerResolvedSorts = options.preserveServerResolvedSorts ?? true;
  // Random, date-added, and reference-count ordering are produced by the server across the whole
  // result set for remote grids; preserve the order the cards arrived in rather than reshuffling
  // the loaded page locally. Detail-page grids without a remote request handler only have local
  // cards, so Random needs a deterministic client shuffle keyed by the current randomSeed.
  if (state.sortBy === "random") {
    return preserveServerResolvedSorts ? filtered : seededShuffle(filtered, state.randomSeed);
  }
  if (state.sortBy === "added" || state.sortBy === "references") {
    return filtered;
  }

  const sorted = filtered.toSorted((left, right) => {
    const direction = state.sortDir === "asc" ? 1 : -1;
    if (state.sortBy === "rating") {
      return (getRatingValue(left.entity.capabilities) - getRatingValue(right.entity.capabilities)) * direction;
    }
    if (state.sortBy === "kind") {
      return left.entity.kind.localeCompare(right.entity.kind) * direction || left.entity.title.localeCompare(right.entity.title);
    }
    if (state.sortBy === "position") {
      const leftPosition = primaryPositionValue(left.entity);
      const rightPosition = primaryPositionValue(right.entity);
      if (leftPosition != null && rightPosition != null && leftPosition !== rightPosition) {
        return (leftPosition - rightPosition) * direction;
      }
      if (leftPosition != null && rightPosition != null) return 0;
      if (leftPosition != null && rightPosition == null) return -1;
      if (leftPosition == null && rightPosition != null) return 1;
      return left.entity.title.localeCompare(right.entity.title);
    }
    return left.entity.title.localeCompare(right.entity.title) * direction;
  });

  if (state.sortBy !== "position") return sorted;

  return sorted.map((card) => {
    if (card.custom?.bottomLeft) return card;
    const sortNum = numberValue(card.entity.sortOrder);
    if (!sortNum) return card;
    if (card.entity.kind === ENTITY_KIND.video) {
      return { ...card, custom: { ...card.custom, bottomLeft: { label: `E${sortNum}`, title: `Episode ${sortNum}` } } };
    }
    if (card.entity.kind === ENTITY_KIND.videoSeason) {
      return { ...card, custom: { ...card.custom, bottomLeft: { label: `S${sortNum}`, title: `Season ${sortNum}` } } };
    }
    return card;
  });
}

/**
 * Serializes the current grid controls into the non-privacy request shape
 * expected by list endpoints and by the thumbnail lab state preview.
 */
export function entityGridRequestFromState(
  state: EntityGridState,
  filterOptions: EntityGridFilterOption[],
): EntityGridRequest {
  const server = buildServerQueryFromFilters(state.filterIds);

  // Only the sorts that the server can reproduce across the full result set are
  // forwarded. "kind" and "position" remain client-only reorderings of the
  // loaded page, so the server keeps its default ordering for them.
  if (state.sortBy === "random") {
    server.sort = "random";
    server.seed = state.randomSeed;
  } else if (state.sortBy === "added") {
    server.sort = "added";
    server.sortDir = state.sortDir;
  } else if (state.sortBy === "rating") {
    server.sort = "rating";
    server.sortDir = state.sortDir;
  } else if (state.sortBy === "title") {
    server.sort = "title";
    server.sortDir = state.sortDir;
  } else if (state.sortBy === "references") {
    server.sort = "references";
    server.sortDir = state.sortDir;
  }

  return {
    filters: state.filterIds
      .map((id) => entityGridFilterFromId(id, filterOptions))
      .filter((option): option is EntityGridFilterOption => Boolean(option)),
    kind: state.activeKind === ENTITY_GRID_ALL_KINDS ? undefined : state.activeKind,
    query: state.query.trim() || undefined,
    sortBy: state.sortBy,
    sortDir: state.sortDir,
    server,
  };
}
