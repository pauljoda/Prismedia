import {
  getCapability,
  getImagesCapability,
  getRatingValue,
  getTechnicalCapability,
  getThumbnailUrl,
  isNsfw,
  type EntityCapabilityKind,
} from "$lib/api/capabilities";
import { numberValue, formatDurationString, durationToSeconds, normalized, formatResolutionLabel } from "$lib/utils/format";
import type { EntityCard, EntityCapability } from "$lib/api/generated/model";
import type { EntityThumbnail } from "$lib/api/generated/model";
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

export type EntityGridSort = "title" | "kind" | "rating" | "position" | "added" | "random";
export type EntityGridSortDir = "asc" | "desc";
export type EntityGridViewMode = "grid" | "list";

/**
 * Server-resolvable list parameters derived from the active grid controls.
 * These map onto the entity list endpoint so sorting, the seeded shuffle, and
 * the library filters apply across the entire matching set instead of only the
 * page already loaded in the browser. Undefined fields are omitted from the
 * request so the server keeps its defaults.
 */
export interface EntityGridServerQuery {
  sort?: EntityGridSort;
  sortDir?: EntityGridSortDir;
  seed?: number;
  favorite?: boolean;
  organized?: boolean;
  ratingMin?: number;
  ratingMax?: number;
  unrated?: boolean;
  status?: string;
}

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

function isFullEntityCard(entity: EntityGridSourceEntity): entity is EntityCard {
  return "capabilities" in entity;
}

function capabilitiesForEntity(entity: EntityGridSourceEntity): EntityCapability[] {
  if (isFullEntityCard(entity)) return entity.capabilities;

  const caps: EntityCapability[] = [];
  if (entity.isFavorite || entity.isNsfw || entity.isOrganized) {
    caps.push({
      kind: "flags" as const,
      isFavorite: entity.isFavorite || null,
      isNsfw: entity.isNsfw || null,
      isOrganized: entity.isOrganized || null,
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

  if (entity.kind === ENTITY_KIND.image && width && height) return { width, height };
  return aspectRatioForKind(entity.kind);
}

function assetFromPath(path: string, title: string, role?: string): EntityThumbnailAsset {
  return {
    alt: role ? `${title} ${role}` : title,
    role,
    src: path,
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
  return path ? assetFromPath(path, entity.title, "preview") : null;
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
    return entity.hoverImages.map((image) => assetFromPath(image.path, image.title, "preview"));
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
  if (!isFullEntityCard(entity) && entity.hoverKind === "sprite" && entity.hoverUrl) {
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
    getThumbnailUrl(capabilities) ??
    images?.coverUrl ??
    images?.items.find(
      (item) =>
        item.kind === ENTITY_FILE_ROLE.cover ||
        item.kind === ENTITY_FILE_ROLE.poster ||
        item.kind === ENTITY_FILE_ROLE.thumbnail ||
        item.kind === ENTITY_FILE_ROLE.logo,
    )?.path ??
    null;

  // Small grid variant paired with the list cover, used for responsive srcset.
  const coverThumbPath = !isFullEntityCard(entity) ? entity.coverThumbUrl ?? null : null;

  const spriteHover = findSpriteHover(entity);
  const imageSequence = previewAssets(entity, [ENTITY_FILE_ROLE.trickplay, ENTITY_FILE_ROLE.sprite]);
  const childSequence = imageSequence.length > 0 ? [] : childPreviewAssets(entity);
  const hover: EntityThumbnailCard["hover"] = spriteHover
    ? { kind: "sprite", spriteUrl: spriteHover.spriteUrl, vttUrl: spriteHover.vttUrl }
    : imageSequence.length > 0
      ? { kind: "image-sequence", assets: imageSequence }
      : childSequence.length > 0
        ? { kind: "image-sequence", assets: childSequence }
      : { kind: "none" };

  return {
    aspectRatio: aspectRatioForEntity(entity),
    cover: coverPath
      ? { ...assetFromPath(coverPath, entity.title, ENTITY_FILE_ROLE.cover), thumbSrc: coverThumbPath ?? undefined }
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
  };
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
    id === "rating:unrated" ||
    id.startsWith("rating:min:") ||
    id.startsWith("rating:max:") ||
    id.startsWith("status:")
  );
}

/**
 * Folds the active filter IDs into the server-resolvable query. Rating bounds
 * collapse to the tightest active min/max; favorite and organized resolve to
 * booleans; the engagement status takes the first selected value.
 */
export function buildServerQueryFromFilters(filterIds: string[]): EntityGridServerQuery {
  const server: EntityGridServerQuery = {};
  for (const id of filterIds) {
    if (id === "flags:favorite") {
      server.favorite = true;
    } else if (id === "flags:organized:true") {
      server.organized = true;
    } else if (id === "flags:organized:false") {
      server.organized = false;
    } else if (id === "rating:unrated") {
      server.unrated = true;
    } else if (id.startsWith("rating:min:")) {
      const value = Number(id.slice("rating:min:".length));
      if (Number.isFinite(value)) server.ratingMin = Math.max(server.ratingMin ?? value, value);
    } else if (id.startsWith("rating:max:")) {
      const value = Number(id.slice("rating:max:".length));
      if (Number.isFinite(value)) server.ratingMax = Math.min(server.ratingMax ?? value, value);
    } else if (STATUS_VALUE_BY_ID.has(id)) {
      server.status = STATUS_VALUE_BY_ID.get(id);
    }
  }
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
export function buildCapabilityFilterOptions(cards: EntityThumbnailCard[]): EntityGridFilterOption[] {
  const options = new Map<string, EntityGridFilterOption>();
  const hasDates = true;
  const hasFiles = true;
  const hasFlags = cards.some((card) => Boolean(getCapability(card.entity.capabilities, CAPABILITY_KIND.flags)));
  const hasProgress = true;
  const hasRating = cards.some((card) => getRatingValue(card.entity.capabilities) > 0);
  const hasTechnical = cards.some((card) => Boolean(getTechnicalCapability(card.entity.capabilities)));

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

  if (hasFiles) {
    addUniqueOption(options, { id: "files:has:true", label: "Has file", capabilityKind: CAPABILITY_KIND.files, value: "has:true" });
    addUniqueOption(options, { id: "files:has:false", label: "No file", capabilityKind: CAPABILITY_KIND.files, value: "has:false" });
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
        case CAPABILITY_KIND.files: {
          const hasEntityFile = capability.items.length > 0;
          addOption(options, {
            id: `files:has:${hasEntityFile ? "true" : "false"}`,
            label: hasEntityFile ? "Has file" : "No file",
            capabilityKind: CAPABILITY_KIND.files,
            value: `has:${hasEntityFile ? "true" : "false"}`,
          });
          break;
        }
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
): EntityThumbnailCard[] {
  const query = state.query.trim().toLowerCase();
  // Server-resolved filters (favorite, organized, rating bounds, unrated, and
  // engagement status) have already pruned the result set on the server, and the
  // list thumbnail does not even carry the data some of them need. Applying them
  // again here would wrongly empty the page, so they are excluded from the local
  // pass and only the genuinely client-side filters are re-checked.
  const filters = state.filterIds
    .filter((id) => !isServerResolvedFilterId(id))
    .map((id) => entityGridFilterFromId(id, filterOptions))
    .filter((option): option is EntityGridFilterOption => Boolean(option));

  const filtered = cards.filter((card) => {
    if (!state.includeNsfw && isNsfw(card.entity.capabilities)) return false;
    if (state.activeKind !== ENTITY_GRID_ALL_KINDS && card.entity.kind !== state.activeKind) return false;
    if (query && !card.entity.title.toLowerCase().includes(query)) return false;
    return filters.every((filter) => entityMatchesFilter(card.entity.capabilities, filter));
  });

  // Random and date-added ordering are produced by the server across the whole
  // result set; preserve the order the cards arrived in rather than reshuffling
  // the loaded page locally.
  if (state.sortBy === "random" || state.sortBy === "added") {
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
