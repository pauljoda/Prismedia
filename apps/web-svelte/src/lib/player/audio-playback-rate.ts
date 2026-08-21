/** Playback rates exposed when an audio queue advertises variable-rate support. */
export const AUDIO_PLAYBACK_RATES = [0.75, 1, 1.25, 1.5, 1.75, 2] as const;

/** Returns the next supported rate, wrapping back to normal speed after the maximum. */
export function nextAudioPlaybackRate(current: number): number {
  const currentIndex = AUDIO_PLAYBACK_RATES.findIndex((rate) => rate === current);
  return AUDIO_PLAYBACK_RATES[(currentIndex + 1) % AUDIO_PLAYBACK_RATES.length];
}

/** Formats a playback rate for the compact shared-player control. */
export function formatAudioPlaybackRate(rate: number): string {
  return `${rate}×`;
}
