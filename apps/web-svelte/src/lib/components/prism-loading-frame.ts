export const PRISM_LOADING_CYCLE_SECONDS = 2.8;

const ARTWORK_WIDTH = 640;
const ARTWORK_HEIGHT = 590;
const PRISM_INLINE_INSET = 12;
const BEAM_OVERSHOOT = 40;
const BEAM_VERTICAL_INSET = 8;
const INCOMING_BEAM_SLOPE = -60 / 263;

export const prismLoadingMetrics = {
  lightLineWidth: 1.5,
  spectrumBandWidth: 7,
  beamGlowRadius: 6,
  spectrumGlowRadius: 8,
  impactGlowRadius: 32,
  internalLightStart: 0.82,
  internalLightLength: 0.18,
} as const;

export interface PrismLoadingFrame {
  incomingLightProgress: number;
  incomingLightOpacity: number;
  prismColorProgress: number;
  spectrumProgress: number;
  spectrumOpacity: number;
  impactGlowOpacity: number;
}

export interface PrismLoadingPoint {
  x: number;
  y: number;
}

export interface PrismLoadingRectangle extends PrismLoadingPoint {
  width: number;
  height: number;
}

export interface PrismLoadingGeometry {
  width: number;
  height: number;
  prism: PrismLoadingRectangle;
  entry: PrismLoadingPoint;
  impact: PrismLoadingPoint;
  spectrumStartX: number;
  spectrumStartTopY: number;
  spectrumStartBottomY: number;
  spectrumTargetX: number;
  incomingStart: PrismLoadingPoint;
}

export const reducedMotionPrismLoadingFrame: PrismLoadingFrame = {
  incomingLightProgress: 1,
  incomingLightOpacity: 0,
  prismColorProgress: 1,
  spectrumProgress: 1,
  spectrumOpacity: 0.72,
  impactGlowOpacity: 0,
};

export function prismLoadingFrameAt(progress: number): PrismLoadingFrame {
  const normalizedProgress = clamp(progress, 0, 1);
  const incomingLightProgress = smoothstep(normalizedProgress, 0.04, 0.38);
  const incomingLightFade = 1 - smoothstep(normalizedProgress, 0.58, 0.76);
  const prismColorProgress =
    smoothstep(normalizedProgress, 0.4, 0.54) * (1 - smoothstep(normalizedProgress, 0.9, 1));
  const spectrumProgress = smoothstep(normalizedProgress, 0.5, 0.84);
  const spectrumOpacity =
    smoothstep(normalizedProgress, 0.48, 0.56) * (1 - smoothstep(normalizedProgress, 0.9, 1));
  const impactGlowOpacity =
    smoothstep(normalizedProgress, 0.37, 0.43) * (1 - smoothstep(normalizedProgress, 0.43, 0.54));

  return {
    incomingLightProgress,
    incomingLightOpacity: incomingLightProgress * incomingLightFade,
    prismColorProgress,
    spectrumProgress,
    spectrumOpacity,
    impactGlowOpacity,
  };
}

export function prismLoadingFrameAtElapsed(elapsedSeconds: number): PrismLoadingFrame {
  const wrappedTime = elapsedSeconds % PRISM_LOADING_CYCLE_SECONDS;
  const positiveTime = wrappedTime < 0 ? wrappedTime + PRISM_LOADING_CYCLE_SECONDS : wrappedTime;
  return prismLoadingFrameAt(positiveTime / PRISM_LOADING_CYCLE_SECONDS);
}

export function prismLoadingGeometry(
  width: number,
  height: number,
  requestedMarkWidth: number,
): PrismLoadingGeometry {
  const safeWidth = Math.max(width, 0);
  const safeHeight = Math.max(height, 0);
  const prismWidth = Math.min(Math.max(requestedMarkWidth, 0), Math.max(safeWidth - PRISM_INLINE_INSET * 2, 0));
  const prismHeight = (prismWidth * ARTWORK_HEIGHT) / ARTWORK_WIDTH;
  const prism = {
    x: (safeWidth - prismWidth) / 2,
    y: (safeHeight - prismHeight) / 2,
    width: prismWidth,
    height: prismHeight,
  };
  const entry = pointInPrism(prism, 149, 349);
  const impact = pointInPrism(prism, 320, 349);
  const incomingStartX = -BEAM_OVERSHOOT;
  const calculatedStartY = entry.y - INCOMING_BEAM_SLOPE * (entry.x - incomingStartX);
  const verticalInset = Math.min(BEAM_VERTICAL_INSET, safeHeight / 2);

  return {
    width: safeWidth,
    height: safeHeight,
    prism,
    entry,
    impact,
    spectrumStartX: prism.x + (prism.width * 447) / ARTWORK_WIDTH,
    spectrumStartTopY: prism.y + (prism.height * 285) / ARTWORK_HEIGHT,
    spectrumStartBottomY: prism.y + (prism.height * 368) / ARTWORK_HEIGHT,
    spectrumTargetX: safeWidth + BEAM_OVERSHOOT,
    incomingStart: {
      x: incomingStartX,
      y: clamp(calculatedStartY, verticalInset, safeHeight - verticalInset),
    },
  };
}

export function interpolatePrismLoadingPoint(
  start: PrismLoadingPoint,
  end: PrismLoadingPoint,
  progress: number,
): PrismLoadingPoint {
  return {
    x: interpolate(start.x, end.x, progress),
    y: interpolate(start.y, end.y, progress),
  };
}

export function prismLoadingSpectrumTarget(
  geometry: PrismLoadingGeometry,
  fraction: number,
): PrismLoadingPoint {
  const verticalInset = Math.min(BEAM_VERTICAL_INSET, geometry.height / 2);
  return {
    x: geometry.spectrumTargetX,
    y: interpolate(verticalInset, geometry.height - verticalInset, fraction),
  };
}

export function prismLoadingSpectrumStart(
  geometry: PrismLoadingGeometry,
  fraction: number,
): PrismLoadingPoint {
  return {
    x: geometry.spectrumStartX,
    y: interpolate(geometry.spectrumStartTopY, geometry.spectrumStartBottomY, fraction),
  };
}

export function prismLoadingBandPath(
  start: PrismLoadingPoint,
  end: PrismLoadingPoint,
  startWidth: number,
  endWidth: number,
): string {
  const startHalfWidth = startWidth / 2;
  const endHalfWidth = endWidth / 2;
  return [
    `M ${start.x} ${start.y - startHalfWidth}`,
    `L ${end.x} ${end.y - endHalfWidth}`,
    `L ${end.x} ${end.y + endHalfWidth}`,
    `L ${start.x} ${start.y + startHalfWidth}`,
    "Z",
  ].join(" ");
}

export function interpolate(start: number, end: number, progress: number): number {
  return start + (end - start) * progress;
}

function pointInPrism(
  prism: PrismLoadingRectangle,
  artworkX: number,
  artworkY: number,
): PrismLoadingPoint {
  return {
    x: prism.x + (prism.width * artworkX) / ARTWORK_WIDTH,
    y: prism.y + (prism.height * artworkY) / ARTWORK_HEIGHT,
  };
}

function smoothstep(value: number, start: number, end: number): number {
  const progress = clamp((value - start) / (end - start), 0, 1);
  return progress * progress * (3 - 2 * progress);
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(Math.max(value, minimum), maximum);
}
