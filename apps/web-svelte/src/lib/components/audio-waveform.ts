export function isLegacyPositiveOnlyWaveform(waveform: number[]): boolean {
  const pairCount = Math.floor(waveform.length / 2);
  if (pairCount <= 0) return false;

  let negativePairs = 0;
  let positivePairs = 0;
  let audiblePairs = 0;

  for (let i = 0; i < pairCount; i += 1) {
    const min = waveform[i * 2] ?? 0;
    const max = waveform[i * 2 + 1] ?? 0;
    if (min < 0) negativePairs += 1;
    if (max > 0) positivePairs += 1;
    if (Math.max(Math.abs(min), Math.abs(max)) > 1) audiblePairs += 1;
  }

  if (audiblePairs === 0) return false;
  return negativePairs / pairCount <= 0.02 && positivePairs / pairCount > 0.02;
}

export function waveformForDisplay(waveform: number[]): number[] | null {
  const pairCount = Math.floor(waveform.length / 2);
  if (pairCount <= 0) return null;
  return waveform;
}
