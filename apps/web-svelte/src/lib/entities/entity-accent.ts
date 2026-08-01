import {
  ENTITY_KIND,
  ENTITY_KIND_DEFINITIONS,
  ENTITY_KIND_ICON,
  THUMBNAIL_META_ICON,
  type EntityKindCode,
} from "$lib/api/generated/codes";
import { colors } from "@prismedia/ui-svelte";
import { isEntityKindCode } from "./entity-codes";
import type { EntityThumbnailMetaIcon } from "./entity-thumbnail";

export interface EntityAccent {
  primary: string;
  secondary: string;
}

export const PRISM_SPECTRUM = {
  ...colors.spectrum,
} as const;

export const PRISM_MATERIAL_SPECTRUM = {
  ...colors.materialSpectrum,
} as const;

/** A hue of the prism spectrum, available as both brand light and muted material paint. */
export type PrismSpectrumHue = keyof typeof PRISM_MATERIAL_SPECTRUM;

/**
 * Hue order of the prism spectrum, from the red end to the magenta end. This is the order
 * dispersed light arrives in, so any surface that lays entity families out as a spectrum should
 * sort by it rather than by an incidental data order.
 */
export const PRISM_SPECTRUM_ORDER = [
  "red",
  "orange",
  "yellow",
  "green",
  "cyan",
  "blue",
  "violet",
  "magenta",
] as const satisfies ReadonlyArray<PrismSpectrumHue>;

/**
 * The hue pair that identifies each entity family, named rather than valued so the material and
 * emitted palettes can never drift apart. Adjacent families share a boundary hue, which is what
 * makes the whole set read as one continuous spectrum.
 */
type EntityHuePair = readonly [PrismSpectrumHue, PrismSpectrumHue];

const FALLBACK_HUES: EntityHuePair = ["cyan", "violet"];
const THUMBNAIL_META_FAMILY_BY_ICON: Partial<Record<EntityThumbnailMetaIcon, EntityKindCode>> = {
  [THUMBNAIL_META_ICON.video]: ENTITY_KIND.video,
  [THUMBNAIL_META_ICON.season]: ENTITY_KIND.video,
  [THUMBNAIL_META_ICON.episode]: ENTITY_KIND.video,
  [THUMBNAIL_META_ICON.image]: ENTITY_KIND.image,
  [THUMBNAIL_META_ICON.gallery]: ENTITY_KIND.image,
  [THUMBNAIL_META_ICON.audio]: ENTITY_KIND.audio,
  [THUMBNAIL_META_ICON.album]: ENTITY_KIND.audio,
  [THUMBNAIL_META_ICON.track]: ENTITY_KIND.audio,
  [THUMBNAIL_META_ICON.disc]: ENTITY_KIND.audio,
  [ENTITY_KIND_ICON.artist]: ENTITY_KIND.audio,
  [THUMBNAIL_META_ICON.book]: ENTITY_KIND.book,
  [THUMBNAIL_META_ICON.volume]: ENTITY_KIND.book,
  [THUMBNAIL_META_ICON.chapter]: ENTITY_KIND.book,
  [THUMBNAIL_META_ICON.page]: ENTITY_KIND.book,
  [ENTITY_KIND_ICON.author]: ENTITY_KIND.book,
  [THUMBNAIL_META_ICON.collection]: ENTITY_KIND.collection,
  [THUMBNAIL_META_ICON.count]: ENTITY_KIND.collection,
  [THUMBNAIL_META_ICON.person]: ENTITY_KIND.person,
  [THUMBNAIL_META_ICON.studio]: ENTITY_KIND.studio,
  [THUMBNAIL_META_ICON.tag]: ENTITY_KIND.tag,
};

function huesForKind(kind: string): EntityHuePair {
  if (!isEntityKindCode(kind)) return FALLBACK_HUES;
  const presentation = ENTITY_KIND_DEFINITIONS[kind].presentation;
  return [presentation.primaryAccent, presentation.secondaryAccent];
}

/**
 * Returns the stable spectrum pair that represents an entity family, in muted material paint.
 * This is the palette for persistent chrome: markers, rails, borders, and state fills.
 */
export function entityAccentForKind(kind: string): EntityAccent {
  const [primary, secondary] = huesForKind(kind);
  return {
    primary: PRISM_MATERIAL_SPECTRUM[primary],
    secondary: PRISM_MATERIAL_SPECTRUM[secondary],
  };
}

/** Matches native thumbnail chips by tinting metadata through its represented entity family. */
export function thumbnailMetaAccentForIcon(icon: EntityThumbnailMetaIcon): string {
  const family = THUMBNAIL_META_FAMILY_BY_ICON[icon];
  return family
    ? entityAccentForKind(family).primary
    : "var(--color-text-muted, #8a93a6)";
}

/**
 * Returns an entity family's spectrum pair in full brand light, matching the prism logo.
 * Reserved for literal emitted-light moments such as the prism mark, the loading beam, and a
 * dispersion chart. Persistent chrome uses {@link entityAccentForKind} instead.
 */
export function entityEmittedAccentForKind(kind: string): EntityAccent {
  const [primary, secondary] = huesForKind(kind);
  return {
    primary: PRISM_SPECTRUM[primary],
    secondary: PRISM_SPECTRUM[secondary],
  };
}

/**
 * Position of an entity family along the prism spectrum, taken from where its primary hue sits
 * in {@link PRISM_SPECTRUM_ORDER}. Unknown families sort last.
 */
export function entitySpectrumIndex(kind: string): number {
  const index = PRISM_SPECTRUM_ORDER.indexOf(huesForKind(kind)[0]);
  return index < 0 ? PRISM_SPECTRUM_ORDER.length : index;
}
