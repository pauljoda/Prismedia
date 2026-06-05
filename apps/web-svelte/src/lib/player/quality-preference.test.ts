import { describe, expect, it } from "vitest";
import { resolvePreferredRung } from "./quality-preference";
import type { PlayerQualityRung } from "$lib/components/video-player-types";

const RUNGS: PlayerQualityRung[] = [
  { name: "8mbps", label: "1080p · 8 Mbps", bitrate: 8_000_000, url: "/8" },
  { name: "4mbps", label: "720p · 4 Mbps", bitrate: 4_000_000, url: "/4" },
  { name: "1500kbps", label: "720p · 1.5 Mbps", bitrate: 1_500_000, url: "/1.5" },
];

describe("resolvePreferredRung", () => {
  it("pins the highest tier at or below a numeric cap", () => {
    expect(resolvePreferredRung(5_000_000, RUNGS)).toBe("4mbps");
    expect(resolvePreferredRung(8_000_000, RUNGS)).toBe("8mbps");
  });

  it("falls back to the lowest tier when the cap is below every tier", () => {
    expect(resolvePreferredRung(500_000, RUNGS)).toBe("1500kbps");
  });

  it("returns null (Auto) for the auto/direct sentinels or when there are no tiers", () => {
    expect(resolvePreferredRung("auto", RUNGS)).toBeNull();
    expect(resolvePreferredRung("direct", RUNGS)).toBeNull();
    expect(resolvePreferredRung(4_000_000, [])).toBeNull();
  });
});
