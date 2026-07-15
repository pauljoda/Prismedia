export const MAX_DISPLAY_WAVEFORM_PAIRS = 4_096;
export const MAX_WAVEFORM_STRIP_WIDTH = 8_192;

/**
 * Reduces a generated min/max envelope to the detail the scrolling player can render safely.
 * Each output bucket preserves the loudest positive and negative samples in its source range.
 */
export function waveformForDisplay(waveform: number[]): number[] | null {
  const pairCount = Math.floor(waveform.length / 2);
  if (pairCount <= 0) return null;
  if (pairCount <= MAX_DISPLAY_WAVEFORM_PAIRS) return waveform;

  const display = new Array<number>(MAX_DISPLAY_WAVEFORM_PAIRS * 2);
  for (let target = 0; target < MAX_DISPLAY_WAVEFORM_PAIRS; target += 1) {
    const sourceStart = Math.floor((target * pairCount) / MAX_DISPLAY_WAVEFORM_PAIRS);
    const sourceEnd = Math.max(
      sourceStart + 1,
      Math.floor(((target + 1) * pairCount) / MAX_DISPLAY_WAVEFORM_PAIRS),
    );
    let bucketMin = 0;
    let bucketMax = 0;

    for (let source = sourceStart; source < sourceEnd; source += 1) {
      const min = waveform[source * 2] ?? 0;
      const max = waveform[source * 2 + 1] ?? 0;
      if (Number.isFinite(min)) bucketMin = Math.min(bucketMin, min);
      if (Number.isFinite(max)) bucketMax = Math.max(bucketMax, max);
    }

    display[target * 2] = bucketMin;
    display[target * 2 + 1] = bucketMax;
  }

  return display;
}

/**
 * Chooses a scrollable waveform width without exceeding conservative browser canvas limits.
 */
export function waveformStripWidth(
  pairCount: number,
  durationSeconds: number,
  containerWidth: number,
): number {
  if (containerWidth <= 0) return 0;

  const naturalWidth = Math.max(1, Math.floor(pairCount) * 2);
  const durationWidth = Math.max(0.001, durationSeconds) * 10;
  const preferredWidth = Math.max(naturalWidth, containerWidth * 6, durationWidth);
  return Math.max(containerWidth, Math.min(preferredWidth, MAX_WAVEFORM_STRIP_WIDTH));
}
