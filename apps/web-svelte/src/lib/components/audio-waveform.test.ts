import { describe, expect, it } from "vitest";
import {
  isLegacyPositiveOnlyWaveform,
  waveformForDisplay,
} from "./audio-waveform";

describe("audio waveform display helpers", () => {
  it("detects stale positive-only waveform caches", () => {
    const stale = [0, 16191, 0, 16255, 0, 32575, 0, 32575];

    expect(isLegacyPositiveOnlyWaveform(stale)).toBe(true);
    expect(waveformForDisplay(stale)).toBe(stale);
  });

  it("keeps signed waveform caches unchanged", () => {
    const signed = [-10, 12, -20, 30, -15, 18, -8, 9];

    expect(isLegacyPositiveOnlyWaveform(signed)).toBe(false);
    expect(waveformForDisplay(signed)).toBe(signed);
  });
});
