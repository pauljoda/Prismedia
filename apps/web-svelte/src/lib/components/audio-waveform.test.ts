import { describe, expect, it } from "vitest";
import {
  MAX_DISPLAY_WAVEFORM_PAIRS,
  MAX_WAVEFORM_STRIP_WIDTH,
  waveformForDisplay,
  waveformStripWidth,
} from "./audio-waveform";

describe("audio waveform display", () => {
  it("bounds a 49-minute waveform to a browser-safe strip width", () => {
    const durationSeconds = 49 * 60 + 29;
    const generatedPairCount = durationSeconds * 20;

    expect(waveformStripWidth(generatedPairCount, durationSeconds, 1_300)).toBe(
      MAX_WAVEFORM_STRIP_WIDTH,
    );
  });

  it("preserves the existing width for ordinary tracks", () => {
    expect(waveformStripWidth(3_600, 180, 600)).toBe(7_200);
  });

  it("downsamples oversized envelopes while preserving bucket extrema", () => {
    const pairCount = MAX_DISPLAY_WAVEFORM_PAIRS * 2;
    const waveform = Array.from({ length: pairCount }, (_, index) => [-(index + 1), index + 1]).flat();

    const result = waveformForDisplay(waveform);

    expect(result).toHaveLength(MAX_DISPLAY_WAVEFORM_PAIRS * 2);
    expect(result?.slice(0, 2)).toEqual([-2, 2]);
    expect(result?.slice(-2)).toEqual([-pairCount, pairCount]);
  });
});
