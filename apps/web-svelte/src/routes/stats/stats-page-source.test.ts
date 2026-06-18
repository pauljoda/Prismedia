import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";

const source = readFileSync("src/routes/stats/+page.svelte", "utf8");

describe("stats page source", () => {
  it("uses the shared playback statistics API and generated code constants", () => {
    expect(source).toContain("fetchPlaybackStatistics");
    expect(source).toContain("PLAYBACK_EVENT_KIND");
    expect(source).toContain("ENTITY_KIND.");
  });

  it("opens on completed plays and charts active daily buckets", () => {
    expect(source).toContain("let eventFilter = $state<EventFilter>(PLAYBACK_EVENT_KIND.completed);");
    expect(source).toContain("const dailyChartBuckets = $derived.by");
    expect(source).toContain("{#each dailyChartBuckets as bucket (bucket.date)}");
  });

  it("renders playback thumbnails through the shared EntityThumbnail component", () => {
    expect(source).toContain("EntityThumbnail");
    expect(source).toContain("entityReferenceToThumbnailCard");
    expect(source).toContain("toAspectRatioNumeric");
    expect(source).not.toContain('aspectRatio: "square"');
    expect(source).not.toContain("placeholder-frame");
    expect(source).not.toContain("{#snippet entityArtwork");
  });
});
