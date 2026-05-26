const DEFAULT_WAVEFORM_SCALE = 1;

/**
 * Returns a stable drawing scale for min/max waveform pairs.
 *
 * Waveform generation can occasionally contain one extreme sample that would
 * flatten every visible bar if the renderer used the absolute maximum. A high
 * percentile keeps real loud sections tall while ignoring isolated bad peaks.
 */
export function waveformDisplayScale(waveform: number[]): number {
  const amplitudes: number[] = [];
  const pairCount = Math.floor(waveform.length / 2);

  for (let i = 0; i < pairCount; i += 1) {
    const min = Math.abs(waveform[i * 2] ?? 0);
    const max = Math.abs(waveform[i * 2 + 1] ?? 0);
    const amplitude = Math.max(min, max);
    if (Number.isFinite(amplitude) && amplitude > 0) {
      amplitudes.push(amplitude);
    }
  }

  if (amplitudes.length === 0) return DEFAULT_WAVEFORM_SCALE;

  amplitudes.sort((a, b) => a - b);
  const percentileIndex = Math.floor((amplitudes.length - 1) * 0.95);
  return Math.max(DEFAULT_WAVEFORM_SCALE, amplitudes[percentileIndex] ?? DEFAULT_WAVEFORM_SCALE);
}

export function normalizeWaveformSample(value: number, scale: number): number {
  if (!Number.isFinite(value) || !Number.isFinite(scale) || scale <= 0) return 0;
  return Math.max(-1, Math.min(1, value / scale));
}
