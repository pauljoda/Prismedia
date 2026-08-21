import { describe, expect, it } from "vitest";
import {
  AUDIO_PLAYBACK_RATES,
  formatAudioPlaybackRate,
  nextAudioPlaybackRate,
} from "./audio-playback-rate";

describe("audio playback rates", () => {
  it("cycles through the supported rates and wraps to the first", () => {
    expect(nextAudioPlaybackRate(1)).toBe(1.25);
    expect(nextAudioPlaybackRate(AUDIO_PLAYBACK_RATES.at(-1)!)).toBe(AUDIO_PLAYBACK_RATES[0]);
  });

  it("starts an unknown persisted value at the first supported rate", () => {
    expect(nextAudioPlaybackRate(1.1)).toBe(AUDIO_PLAYBACK_RATES[0]);
  });

  it("uses a compact multiplication label", () => {
    expect(formatAudioPlaybackRate(1.5)).toBe("1.5×");
  });
});
