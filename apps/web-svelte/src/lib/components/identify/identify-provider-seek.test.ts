import { describe, expect, it } from "vitest";
import { providerSeekOrder } from "./identify-provider-seek";

describe("providerSeekOrder", () => {
  it("starts after the active provider and stops at the end of the list", () => {
    expect(providerSeekOrder(["anilist", "tmdb", "stash-xvideos", "stash-xnxx"], "tmdb")).toEqual([
      "stash-xvideos",
      "stash-xnxx",
    ]);
  });

  it("wraps to the beginning only when the active provider is already at the end", () => {
    expect(providerSeekOrder(["anilist", "tmdb", "stash-xvideos"], "stash-xvideos")).toEqual([
      "anilist",
      "tmdb",
      "stash-xvideos",
    ]);
  });
});
