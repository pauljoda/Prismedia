import { PRISM_SPECTRUM } from "$lib/entities/entity-accent";

/**
 * The prism spectrum traversed from its cool end to its warm end, used as a sequential scale for
 * magnitude. Cool reads as quiet and warm as busy, so one glance at hue gives the reading before
 * any label is read. Every chart on a page should ramp in the same direction or the hues stop
 * meaning anything.
 *
 * The scale starts at cyan rather than violet on purpose: the spectrum's violet and blue are too
 * dark to hold their own against a true-black canvas, so a column sitting in that part of the
 * ramp would read as empty. Violet and magenta stay reserved for the prism dispersion, where they
 * identify entity families instead of magnitude.
 */
const HEAT_STOPS = [
  PRISM_SPECTRUM.cyan,
  PRISM_SPECTRUM.green,
  PRISM_SPECTRUM.yellow,
  PRISM_SPECTRUM.orange,
  PRISM_SPECTRUM.red,
] as const;

interface Rgb {
  r: number;
  g: number;
  b: number;
}

function parseHex(hex: string): Rgb {
  const value = Number.parseInt(hex.replace("#", ""), 16);
  return { r: (value >> 16) & 0xff, g: (value >> 8) & 0xff, b: value & 0xff };
}

const HEAT_RGB: readonly Rgb[] = HEAT_STOPS.map(parseHex);

function clamp01(value: number): number {
  if (!Number.isFinite(value)) return 0;
  return Math.min(1, Math.max(0, value));
}

/**
 * Samples the heat scale at `t`, where 0 is the coolest stop and 1 the warmest.
 *
 * @param t Position along the scale, clamped to 0 through 1.
 * @param alpha Optional opacity, so a dense grid can keep quiet cells dark while still tinting them.
 */
export function prismHeatColor(t: number, alpha = 1): string {
  const position = clamp01(t) * (HEAT_RGB.length - 1);
  const lower = Math.floor(position);
  const upper = Math.min(HEAT_RGB.length - 1, lower + 1);
  const blend = position - lower;

  const from = HEAT_RGB[lower];
  const to = HEAT_RGB[upper];
  const mix = (start: number, end: number) => Math.round(start + (end - start) * blend);
  const channels = `${mix(from.r, to.r)} ${mix(from.g, to.g)} ${mix(from.b, to.b)}`;

  const opacity = clamp01(alpha);
  return opacity >= 1 ? `rgb(${channels})` : `rgb(${channels} / ${opacity.toFixed(3)})`;
}

/** SVG gradient stop along the heat scale. */
export interface PrismHeatStop {
  offset: number;
  color: string;
}

/**
 * The heat scale as SVG gradient stops. Filling a whole series from one gradient in user space
 * means a column's colour is set by how tall it is, which is the same encoding the heatmap uses.
 */
export const PRISM_HEAT_STOPS: readonly PrismHeatStop[] = HEAT_STOPS.map((color, index) => ({
  offset: index / (HEAT_STOPS.length - 1),
  color,
}));
