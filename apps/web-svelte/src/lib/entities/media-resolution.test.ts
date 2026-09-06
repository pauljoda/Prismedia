import { describe, expect, it } from "vitest";
import { MEDIA_RESOLUTION_TIER as TIER, MEDIA_RESOLUTION_TIERS } from "$lib/api/generated/codes";
import { resolutionBadge } from "./media-resolution";

describe("shared source resolution", () => {
  it.each([
    [7680, 3200, TIER.uhd8K], [3840, 1600, TIER.uhd4K], [2560, 1080, TIER.qhd],
    [1920, 808, TIER.fullHd], [1440, 1080, TIER.fullHd], [1280, 544, TIER.hd],
    [720, 576, TIER.standard480], [320, 240, TIER.sd], [null, 1080, TIER.fullHd],
    ["1920", null, TIER.fullHd],
  ])("classifies %s × %s as %s", (width, height, tier) => {
    expect(resolutionBadge(width, height)).toBe(tier);
  });

  it.each([[null, null], [0, 0], [-1, null], [Infinity, NaN], ["invalid", undefined]])(
    "does not classify unknown dimensions %s × %s", (width, height) => {
      expect(resolutionBadge(width, height)).toBeNull();
    },
  );

  it("uses every generated width and height threshold in priority order", () => {
    for (const tier of MEDIA_RESOLUTION_TIERS) {
      expect(resolutionBadge(tier.minimumWidth, null)).toBe(tier.code);
      expect(resolutionBadge(null, tier.minimumHeight)).toBe(tier.code);
    }
  });
});
