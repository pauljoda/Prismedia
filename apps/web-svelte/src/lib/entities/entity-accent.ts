import { ENTITY_KIND_DEFINITIONS } from "$lib/api/generated/codes";
import { colors } from "@prismedia/ui-svelte";
import { isEntityKindCode } from "./entity-codes";

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
