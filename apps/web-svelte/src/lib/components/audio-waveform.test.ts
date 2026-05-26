import { describe, expect, it } from "vitest";
import { waveformDisplayScale } from "./audio-waveform";

describe("audio waveform display helpers", () => {
  it("uses a percentile scale so isolated bad peaks do not flatten the waveform", () => {
    const waveform = [
      -100, 100,
      -110, 120,
      -90, 105,
      -95, 100,
      -100, 115,
      -32768, 0,
    ];

    expect(waveformDisplayScale(waveform)).toBeLessThan(32768);
    expect(waveformDisplayScale(waveform)).toBeGreaterThanOrEqual(120);
  });

  it("falls back to one for silent or empty waveforms", () => {
    expect(waveformDisplayScale([])).toBe(1);
    expect(waveformDisplayScale([0, 0, 0, 0])).toBe(1);
  });
});
