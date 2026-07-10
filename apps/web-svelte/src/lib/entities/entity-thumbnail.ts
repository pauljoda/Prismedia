import { THUMBNAIL_HOVER_KIND } from "$lib/api/generated/codes";
import type { EntityCapability, EntityCard, EntityThumbnail, EntityKind } from "$lib/api/generated/model";
import { ENTITY_KIND, resolveEntityHref, type EntityRouteContext } from "./entity-codes";

/** Standard thumbnail shapes used by global entity cards before route-specific layout chooses a size. */
export type EntityThumbnailAspectRatio =
  | "square"
  | "video"
  | "poster"
  | "portrait"
  | "wide"
  | {
      width: number;
      height: number;
    };

/** Renderable image asset for covers, trickplay frames, and gallery/book preview frames. */
export interface EntityThumbnailAsset {
  src: string;
  alt: string;
  role?: string;
  /**
   * Optional small grid-sized variant of {@link src} (480w). When present the
   * cover `<img>` uses it (with {@link thumbSrc2x}) as the srcset, so cards
   * never download the full-resolution `src`; the original stays reserved for
   * detail heroes and the lightbox.
   */
  thumbSrc?: string;
  /**
   * Optional double-density companion of {@link thumbSrc} (960w) for
   * high-DPI displays.
   */
  thumbSrc2x?: string;
  /**
   * Owning entity id when this asset is the cover/preview frame of a distinct
   * child entity (e.g. a gallery's representative cover image). Carried so the
   * feed can hydrate and autoplay the child media that the cover stands in for;
   * absent when the asset belongs to the card's own entity.
   */
  entityId?: string;
}

/** Hover preview behavior supported by the shared thumbnail surface. */
export type EntityThumbnailHoverPreview =
  | {
      kind: typeof THUMBNAIL_HOVER_KIND.none;
    }
  | {
      kind: typeof THUMBNAIL_HOVER_KIND.trickplay | typeof THUMBNAIL_HOVER_KIND.imageSequence;
      assets: EntityThumbnailAsset[];
    }
  | {
      kind: typeof THUMBNAIL_HOVER_KIND.sprite;
      spriteUrl?: string;
      vttUrl: string;
    };

/** Icon vocabulary for compact thumbnail metadata chips. */
export type EntityThumbnailMetaIcon =
  | "audio"
  | "book"
  | "calendar"
  | "chapter"
  | "collection"
  | "count"
  | "duration"
  | "gallery"
  | "image"
  | "person"
  | "studio"
  | "tag"
  | "video";

/** Small count, duration, position, or classification item shown under a thumbnail. */
export interface EntityThumbnailMetaItem {
  icon: EntityThumbnailMetaIcon;
  label: string;
}

/** Entity-specific thumbnail overlay content owned by route/entity mappers. */
export interface EntityThumbnailCustomOverlay {
  bottomLeft?: {
    label: string;
    title?: string;
  };
  /** Attribution chip pinned to the artwork's bottom-left (e.g. the plugin that sourced a discover result). */
  sourceTag?: {
    label: string;
    title?: string;
  };
}

/** Entity payload consumed by the shared thumbnail surface. */
export interface EntityThumbnailEntity {
  id: string;
  kind: EntityKind;
  title: string;
  parentEntityId: string | null;
  parentKind?: EntityKind | null;
  sortOrder: number | string | null;
  capabilities: EntityCapability[];
  childrenByKind: EntityCard["childrenByKind"];
  relationships: EntityCard["relationships"];
}

/** Complete view model for one global entity thumbnail. */
export interface EntityThumbnailCard {
  entity: EntityThumbnailEntity;
  aspectRatio: EntityThumbnailAspectRatio;
  cover: EntityThumbnailAsset | null;
  custom?: EntityThumbnailCustomOverlay;
  fit?: "contain" | "cover";
  hover: EntityThumbnailHoverPreview;
  /** True when this Entity or its structural descendants own source media. */
  hasSourceMedia?: boolean;
  href?: string;
  meta?: EntityThumbnailMetaItem[];
  /** Fraction watched/read in 0..1 for the thumbnail progress meter, when meaningful. */
  progress?: number | null;
  /**
   * For a wanted placeholder, its latest acquisition status code (an AcquisitionStatus value), so the
   * thumbnail's wanted badge shows what the item is doing. Null/undefined when not wanted or unknown.
   */
  wantedStatus?: string | null;
  /** Latest linked acquisition state, retained after an entity leaves Wanted. */
  latestAcquisitionStatus?: string | null;
  /**
   * Distinct acquisition states across the Entity's structural subtree. Availability filters use
   * membership here; older servers may omit it, in which case callers fall back to the singular status.
   */
  acquisitionStatuses?: string[] | null;
  routeContext?: EntityRouteContext;
  subtitle?: string;
}

export interface EntityReferenceThumbnailOptions {
  aspectRatio?: EntityThumbnailAspectRatio;
  cover?: EntityThumbnailAsset | null;
  fit?: "contain" | "cover";
  href?: string;
  hover?: EntityThumbnailHoverPreview;
  meta?: EntityThumbnailMetaItem[];
  routeContext?: EntityRouteContext;
  subtitle?: string;
}

/** Converts a named or numeric entity aspect ratio into a CSS aspect-ratio value. */
export function toAspectRatioValue(ratio: EntityThumbnailAspectRatio): string {
  if (typeof ratio === "string") {
    switch (ratio) {
      case "poster":
        return "2 / 3";
      case "portrait":
        return "3 / 4";
      case "square":
        return "1 / 1";
      case "wide":
        return "21 / 9";
      case "video":
      default:
        return "16 / 9";
    }
  }

  if (!Number.isFinite(ratio.width) || !Number.isFinite(ratio.height) || ratio.width <= 0 || ratio.height <= 0) {
    return "16 / 9";
  }

  return `${ratio.width} / ${ratio.height}`;
}

/** Converts an aspect ratio to a numeric width/height value for CSS calculations. */
export function toAspectRatioNumeric(ratio: EntityThumbnailAspectRatio): number {
  if (typeof ratio === "string") {
    switch (ratio) {
      case "poster": return 2 / 3;
      case "portrait": return 3 / 4;
      case "square": return 1;
      case "wide": return 21 / 9;
      case "video":
      default: return 16 / 9;
    }
  }
  if (!Number.isFinite(ratio.width) || !Number.isFinite(ratio.height) || ratio.width <= 0 || ratio.height <= 0) {
    return 16 / 9;
  }
  return ratio.width / ratio.height;
}

/** Chooses the default thumbnail frame for a referenced entity kind. */
export function aspectRatioForKind(kind: string): EntityThumbnailAspectRatio {
  if (kind === ENTITY_KIND.video) return "video";
  if (kind === ENTITY_KIND.movie || kind === ENTITY_KIND.videoSeries || kind === ENTITY_KIND.videoSeason) return "poster";
  if (kind === ENTITY_KIND.book || kind === ENTITY_KIND.bookAuthor || kind === ENTITY_KIND.bookChapter || kind === ENTITY_KIND.bookPage || kind === ENTITY_KIND.bookVolume) return "poster";
  if (kind === ENTITY_KIND.person) return { width: 4, height: 5 };
  if (kind === ENTITY_KIND.studio) return "wide";
  if (kind === ENTITY_KIND.collection) return "video";
  return "square";
}

/** Resolves the link owned by a thumbnail card, using explicit overrides before entity defaults. */
export function resolveEntityThumbnailHref(card: EntityThumbnailCard): string | undefined {
  return card.href ?? resolveEntityHref(card.entity.kind, card.entity.id, card.routeContext);
}

/** Builds a lightweight thumbnail card from a referenced entity. */
export function entityReferenceToThumbnailCard(
  entity: Pick<EntityCard, "id" | "kind" | "title"> & { thumbnailUrl?: string | null },
  options: EntityReferenceThumbnailOptions = {},
): EntityThumbnailCard {
  const thumbUrl = entity.thumbnailUrl;
  const cover = options.cover ?? (thumbUrl ? { src: thumbUrl, alt: entity.title } : null);
  return {
    aspectRatio: options.aspectRatio ?? aspectRatioForKind(entity.kind),
    cover,
    entity: {
      id: entity.id,
      kind: entity.kind,
      title: entity.title,
      parentEntityId: null,
      parentKind: null,
      sortOrder: null,
      capabilities: [],
      childrenByKind: [],
      relationships: [],
    },
    fit: options.fit ?? "cover",
    hover: options.hover ?? { kind: THUMBNAIL_HOVER_KIND.none },
    hasSourceMedia: false,
    href: options.href,
    meta: options.meta,
    routeContext: options.routeContext,
    subtitle: options.subtitle,
  };
}

export function thumbnailToEntityShell(entity: EntityThumbnail): EntityThumbnailEntity {
  return {
    id: entity.id,
    kind: entity.kind,
    title: entity.title,
    parentEntityId: entity.parentEntityId,
    parentKind: entity.parentKind,
    sortOrder: entity.sortOrder,
    capabilities: [],
    childrenByKind: [],
    relationships: [],
  };
}

/** Returns whether a card has enough preview assets to respond to hover or focus. */
export function hasHoverPreview(card: EntityThumbnailCard): boolean {
  if (card.hover.kind === THUMBNAIL_HOVER_KIND.none) return false;
  if (card.hover.kind === THUMBNAIL_HOVER_KIND.sprite) return true;
  return card.hover.assets.length > 0;
}

/** Selects the hover frame nearest the current pointer position across the thumbnail. */
export function pickHoverAsset(card: EntityThumbnailCard, pointerRatio: number): EntityThumbnailAsset | null {
  if (card.hover.kind === THUMBNAIL_HOVER_KIND.none || card.hover.kind === THUMBNAIL_HOVER_KIND.sprite) return null;
  if (card.hover.assets.length === 0) return null;
  const boundedRatio = Math.min(Math.max(pointerRatio, 0), 1);
  const index = Math.min(card.hover.assets.length - 1, Math.floor(boundedRatio * card.hover.assets.length));
  return card.hover.assets[index] ?? null;
}

/** Maps an entity kind code to the closest matching thumbnail meta icon for placeholder display. */
export function iconForKind(kind: string): EntityThumbnailMetaIcon {
  if (kind === ENTITY_KIND.audio || kind === ENTITY_KIND.audioLibrary || kind === ENTITY_KIND.audioTrack) return "audio";
  if (kind === ENTITY_KIND.book || kind === ENTITY_KIND.bookAuthor || kind === ENTITY_KIND.bookChapter || kind === ENTITY_KIND.bookPage || kind === ENTITY_KIND.bookVolume) return "book";
  if (kind === ENTITY_KIND.movie || kind === ENTITY_KIND.video || kind === ENTITY_KIND.videoSeason || kind === ENTITY_KIND.videoSeries) return "video";
  if (kind === ENTITY_KIND.gallery) return "gallery";
  if (kind === ENTITY_KIND.image) return "image";
  if (kind === ENTITY_KIND.person) return "person";
  if (kind === ENTITY_KIND.studio) return "studio";
  if (kind === ENTITY_KIND.tag) return "tag";
  return "collection";
}

const PLACEHOLDER_GRADIENTS = [
  "linear-gradient(135deg, #1a1028 0%, #2d1b4e 40%, #4a2040 100%)",
  "linear-gradient(135deg, #0f1a2e 0%, #1b3a5c 40%, #0d2847 100%)",
  "linear-gradient(135deg, #1a0f0a 0%, #3d2415 40%, #5c3a1b 100%)",
  "linear-gradient(135deg, #0a1a14 0%, #153d2b 40%, #1b5c3f 100%)",
  "linear-gradient(135deg, #1a1018 0%, #3d1535 40%, #5c1b4a 100%)",
  "linear-gradient(135deg, #1a180a 0%, #3d3515 40%, #5c4f1b 100%)",
  "linear-gradient(135deg, #0a0f1a 0%, #15243d 40%, #1b365c 100%)",
  "linear-gradient(135deg, #1a0a12 0%, #3d1528 40%, #5c1b3b 100%)",
];

/** Picks a deterministic gradient background from the palette based on the entity title. */
export function placeholderGradient(title: string): string {
  let hash = 0;
  for (let i = 0; i < title.length; i++) {
    hash = (hash * 31 + title.charCodeAt(i)) >>> 0;
  }
  return PLACEHOLDER_GRADIENTS[hash % PLACEHOLDER_GRADIENTS.length];
}

/** Resolves the currently visible asset, preferring hover previews when active. */
export function getThumbnailAsset(card: EntityThumbnailCard, pointerRatio: number | null): EntityThumbnailAsset | null {
  if (pointerRatio !== null) {
    const hoverAsset = pickHoverAsset(card, pointerRatio);
    if (hoverAsset) return hoverAsset;
  }

  return card.cover;
}
