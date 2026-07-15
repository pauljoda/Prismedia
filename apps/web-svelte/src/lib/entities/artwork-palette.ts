export interface ArtworkPalette {
  primary: string;
  secondary: string;
  background: string;
}

type Rgb = [number, number, number];
type Oklab = [number, number, number];

interface HistogramBin {
  red: number;
  green: number;
  blue: number;
  weight: number;
  edgeWeight: number;
}

interface ColorSamples {
  colors: Oklab[];
  weights: number[];
  edgeWeights: number[];
}

const MAX_SAMPLE_DIMENSION = 96;
const CLUSTER_LIMIT = 8;
const ITERATION_LIMIT = 8;
interface CachedImagePalette {
  source: string;
  palette: ArtworkPalette | null;
}

const paletteCache = new WeakMap<HTMLImageElement, CachedImagePalette>();

function clamp(value: number, minimum = 0, maximum = 1): number {
  return Math.min(Math.max(value, minimum), maximum);
}

function linearComponent(value: number): number {
  return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
}

function encodedComponent(value: number): number {
  const safe = clamp(value);
  return safe <= 0.0031308 ? 12.92 * safe : 1.055 * safe ** (1 / 2.4) - 0.055;
}

function rgbToOklab([red, green, blue]: Rgb): Oklab {
  const linearRed = linearComponent(red);
  const linearGreen = linearComponent(green);
  const linearBlue = linearComponent(blue);
  const l = 0.4122214708 * linearRed + 0.5363325363 * linearGreen + 0.0514459929 * linearBlue;
  const m = 0.2119034982 * linearRed + 0.6806995451 * linearGreen + 0.1073969566 * linearBlue;
  const s = 0.0883024619 * linearRed + 0.2817188376 * linearGreen + 0.6299787005 * linearBlue;
  const lRoot = Math.cbrt(l);
  const mRoot = Math.cbrt(m);
  const sRoot = Math.cbrt(s);
  return [
    0.2104542553 * lRoot + 0.793617785 * mRoot - 0.0040720468 * sRoot,
    1.9779984951 * lRoot - 2.428592205 * mRoot + 0.4505937099 * sRoot,
    0.0259040371 * lRoot + 0.7827717662 * mRoot - 0.808675766 * sRoot,
  ];
}

function oklabToRgb([lightness, a, b]: Oklab): Rgb {
  const lRoot = lightness + 0.3963377774 * a + 0.2158037573 * b;
  const mRoot = lightness - 0.1055613458 * a - 0.0638541728 * b;
  const sRoot = lightness - 0.0894841775 * a - 1.291485548 * b;
  const l = lRoot ** 3;
  const m = mRoot ** 3;
  const s = sRoot ** 3;
  return [
    encodedComponent(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
    encodedComponent(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
    encodedComponent(-0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s),
  ];
}

function squaredDistance(left: Oklab, right: Oklab): number {
  return (left[0] - right[0]) ** 2 + (left[1] - right[1]) ** 2 + (left[2] - right[2]) ** 2;
}

function addWeighted(target: Oklab, color: Oklab, weight: number): void {
  target[0] += color[0] * weight;
  target[1] += color[1] * weight;
  target[2] += color[2] * weight;
}

function relativeLuminance(color: Rgb): number {
  return 0.2126 * linearComponent(color[0]) + 0.7152 * linearComponent(color[1]) + 0.0722 * linearComponent(color[2]);
}

function contrastRatio(left: Rgb, right: Rgb): number {
  const lighter = Math.max(relativeLuminance(left), relativeLuminance(right));
  const darker = Math.min(relativeLuminance(left), relativeLuminance(right));
  return (lighter + 0.05) / (darker + 0.05);
}

function materialAccent(color: Oklab, background: Rgb): Rgb {
  const toned: Oklab = [...color];
  const chroma = Math.hypot(toned[1], toned[2]);
  if (chroma > 0.15) {
    const scale = 0.15 / chroma;
    toned[1] *= scale;
    toned[2] *= scale;
  }
  toned[0] = clamp(toned[0], 0.48, 0.66);

  const original = oklabToRgb(toned);
  if (contrastRatio(original, background) >= 3) return original;

  for (let lightness = toned[0] + 0.02; lightness <= 0.72; lightness += 0.02) {
    const candidate = oklabToRgb([lightness, toned[1], toned[2]]);
    if (contrastRatio(candidate, background) >= 3) return candidate;
  }

  return oklabToRgb([0.72, toned[1] * 0.8, toned[2] * 0.8]);
}

function darkBackground(color: Oklab): Rgb {
  const next: Oklab = [...color];
  const chroma = Math.hypot(next[1], next[2]);
  if (chroma > 0.14) {
    const scale = 0.14 / chroma;
    next[1] *= scale;
    next[2] *= scale;
  }
  next[0] = clamp(next[0] * 0.42, 0.1, 0.24);
  return oklabToRgb(next);
}

function toHex(color: Rgb): string {
  return `#${color.map((value) => Math.round(clamp(value) * 255).toString(16).padStart(2, "0")).join("")}`;
}

function histogramSamples(pixels: Uint8ClampedArray, width: number, height: number): ColorSamples {
  const histogram = new Map<number, HistogramBin>();
  const edgeDepth = Math.max(1, Math.floor(Math.min(width, height) / 8));
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const offset = (y * width + x) * 4;
      const alpha = pixels[offset + 3] / 255;
      if (alpha < 0.05) continue;
      const red = pixels[offset] / 255;
      const green = pixels[offset + 1] / 255;
      const blue = pixels[offset + 2] / 255;
      const key = ((pixels[offset] >> 3) << 10) | ((pixels[offset + 1] >> 3) << 5) | (pixels[offset + 2] >> 3);
      const isEdge = x < edgeDepth || x >= width - edgeDepth || y < edgeDepth || y >= height - edgeDepth;
      const bin = histogram.get(key) ?? { red: 0, green: 0, blue: 0, weight: 0, edgeWeight: 0 };
      bin.red += red * alpha;
      bin.green += green * alpha;
      bin.blue += blue * alpha;
      bin.weight += alpha;
      bin.edgeWeight += isEdge ? alpha : 0;
      histogram.set(key, bin);
    }
  }

  const colors: Oklab[] = [];
  const weights: number[] = [];
  const edgeWeights: number[] = [];
  for (const key of [...histogram.keys()].sort((left, right) => left - right)) {
    const bin = histogram.get(key)!;
    if (bin.weight <= 0) continue;
    colors.push(rgbToOklab([bin.red / bin.weight, bin.green / bin.weight, bin.blue / bin.weight]));
    weights.push(bin.weight);
    edgeWeights.push(bin.edgeWeight);
  }
  return { colors, weights, edgeWeights };
}

function seedScore(color: Oklab, weight: number, centers: Oklab[]): number {
  return Math.min(...centers.map((center) => squaredDistance(color, center))) * Math.sqrt(weight);
}

function initialCenters(samples: ColorSamples, count: number): Oklab[] {
  let firstIndex = 0;
  for (let index = 1; index < samples.colors.length; index += 1) {
    if (samples.weights[index] + 2 * samples.edgeWeights[index] > samples.weights[firstIndex] + 2 * samples.edgeWeights[firstIndex]) {
      firstIndex = index;
    }
  }
  const centers: Oklab[] = [[...samples.colors[firstIndex]]];
  while (centers.length < count) {
    let nextIndex = firstIndex;
    let bestScore = -1;
    for (let index = 0; index < samples.colors.length; index += 1) {
      const score = seedScore(samples.colors[index], samples.weights[index], centers);
      if (score > bestScore) {
        bestScore = score;
        nextIndex = index;
      }
    }
    const candidate = samples.colors[nextIndex];
    if (centers.some((center) => squaredDistance(center, candidate) <= 0.000001)) break;
    centers.push([...candidate]);
  }
  return centers;
}

function nearestCenter(color: Oklab, centers: Oklab[]): number {
  let nearest = 0;
  for (let index = 1; index < centers.length; index += 1) {
    if (squaredDistance(color, centers[index]) < squaredDistance(color, centers[nearest])) nearest = index;
  }
  return nearest;
}

function clusteredColors(samples: ColorSamples): ColorSamples {
  const centers = initialCenters(samples, Math.min(CLUSTER_LIMIT, samples.colors.length));
  let assignments = new Array<number>(samples.colors.length).fill(0);
  for (let iteration = 0; iteration < ITERATION_LIMIT; iteration += 1) {
    assignments = samples.colors.map((color) => nearestCenter(color, centers));
    const sums = centers.map<Oklab>(() => [0, 0, 0]);
    const totals = centers.map(() => 0);
    samples.colors.forEach((color, index) => {
      const cluster = assignments[index];
      addWeighted(sums[cluster], color, samples.weights[index]);
      totals[cluster] += samples.weights[index];
    });
    let movement = 0;
    centers.forEach((center, index) => {
      if (totals[index] <= 0) return;
      const next: Oklab = [sums[index][0] / totals[index], sums[index][1] / totals[index], sums[index][2] / totals[index]];
      movement = Math.max(movement, squaredDistance(center, next));
      centers[index] = next;
    });
    if (movement < 0.000004) break;
  }

  assignments = samples.colors.map((color) => nearestCenter(color, centers));
  const weights = centers.map(() => 0);
  const edgeWeights = centers.map(() => 0);
  assignments.forEach((cluster, index) => {
    weights[cluster] += samples.weights[index];
    edgeWeights[cluster] += samples.edgeWeights[index];
  });
  const populated = centers.map((_, index) => index).filter((index) => weights[index] > 0);
  return {
    colors: populated.map((index) => centers[index]),
    weights: populated.map((index) => weights[index]),
    edgeWeights: populated.map((index) => edgeWeights[index]),
  };
}

function bestAccentIndex(samples: ColorSamples, excluded: Set<number>, references: number[], totalWeight: number): number | undefined {
  let bestIndex: number | undefined;
  let bestScore = -1;
  samples.colors.forEach((color, index) => {
    if (excluded.has(index) || samples.weights[index] / totalWeight < 0.02) return;
    const chroma = Math.hypot(color[1], color[2]);
    const distance = Math.min(...references.map((reference) => Math.sqrt(squaredDistance(color, samples.colors[reference]))));
    const score = Math.sqrt(samples.weights[index] / totalWeight) * (0.08 + chroma) * (0.2 + distance);
    if (score > bestScore) {
      bestScore = score;
      bestIndex = index;
    }
  });
  return bestIndex;
}

function semanticPalette(samples: ColorSamples): ArtworkPalette | null {
  if (samples.colors.length === 0) return null;
  const totalWeight = samples.weights.reduce((sum, value) => sum + value, 0);
  const totalEdgeWeight = Math.max(samples.edgeWeights.reduce((sum, value) => sum + value, 0), 1);
  let backgroundIndex = 0;
  let backgroundScore = -1;
  samples.colors.forEach((_, index) => {
    const score = 0.7 * (samples.weights[index] / totalWeight) + 0.3 * (samples.edgeWeights[index] / totalEdgeWeight);
    if (score > backgroundScore) {
      backgroundScore = score;
      backgroundIndex = index;
    }
  });
  const primaryIndex = bestAccentIndex(samples, new Set([backgroundIndex]), [backgroundIndex], totalWeight) ?? backgroundIndex;
  const secondaryIndex = bestAccentIndex(samples, new Set([backgroundIndex, primaryIndex]), [backgroundIndex, primaryIndex], totalWeight) ?? backgroundIndex;
  const background = darkBackground(samples.colors[backgroundIndex]);
  const primary = materialAccent(samples.colors[primaryIndex], background);
  let secondary = materialAccent(samples.colors[secondaryIndex], background);
  if (Math.sqrt(squaredDistance(rgbToOklab(primary), rgbToOklab(secondary))) < 0.08) {
    const adjusted: Oklab = [...samples.colors[secondaryIndex]];
    adjusted[0] = clamp(adjusted[0], 0.56, 0.66);
    adjusted[1] *= 0.55;
    adjusted[2] *= 0.55;
    secondary = materialAccent(adjusted, background);
  }
  return { background: toHex(background), primary: toHex(primary), secondary: toHex(secondary) };
}

/**
 * Extracts a semantic artwork palette with the native app's standard pipeline:
 * 5-bit histogram, edge-aware OKLab k-means, chroma/distance accent scoring,
 * dark-background normalization, and controlled material tones with 3:1
 * non-text contrast. The extracted hue survives without becoming neon UI light.
 */
export function paletteFromPixels(pixels: Uint8ClampedArray, width: number, height: number): ArtworkPalette | null {
  if (width <= 0 || height <= 0 || pixels.length !== width * height * 4) return null;
  const samples = histogramSamples(pixels, width, height);
  return samples.colors.length > 0 ? semanticPalette(clusteredColors(samples)) : null;
}

/** Samples an image element the UI has already decoded, without another network request. */
export function paletteFromImage(image: HTMLImageElement): ArtworkPalette | null {
  if (typeof document === "undefined" || image.naturalWidth <= 0 || image.naturalHeight <= 0) return null;
  const source = image.currentSrc || image.src;
  const cached = paletteCache.get(image);
  if (cached?.source === source) return cached.palette;
  const scale = Math.min(1, MAX_SAMPLE_DIMENSION / Math.max(image.naturalWidth, image.naturalHeight));
  const width = Math.max(1, Math.round(image.naturalWidth * scale));
  const height = Math.max(1, Math.round(image.naturalHeight * scale));
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext("2d", { willReadFrequently: true });
  if (!context) return null;
  try {
    context.drawImage(image, 0, 0, width, height);
    const palette = paletteFromPixels(context.getImageData(0, 0, width, height).data, width, height);
    paletteCache.set(image, { source, palette });
    return palette;
  } catch {
    paletteCache.set(image, { source, palette: null });
    return null;
  }
}
