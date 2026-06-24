import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";

const source = readFileSync("src/routes/stats/+page.svelte", "utf8");

describe("stats page source", () => {
  it("uses the shared playback statistics API and generated code constants", () => {
    expect(source).toContain("fetchPlaybackStatistics");
    expect(source).toContain("PLAYBACK_EVENT_KIND");
    expect(source).toContain("ENTITY_KIND.");
  });

  it("opens on completed plays and exposes selectable daily buckets", () => {
    expect(source).toContain("let eventFilter = $state<EventFilter>(PLAYBACK_EVENT_KIND.completed);");
    expect(source).toContain("const dailyChartBuckets = $derived.by");
    expect(source).toContain("const selectedChartBucket = $derived.by");
    expect(source).toContain("{#each dailyChartBuckets as bucket (bucket.date)}");
    expect(source).toContain("aria-pressed={selectedChartBucket?.date === bucket.date}");
    expect(source).toContain("onclick={() => selectChartBucket(bucket.date)}");
  });

  it("hydrates playback thumbnails through the shared thumbnail pipeline", () => {
    expect(source).toContain("EntityThumbnail");
    expect(source).toContain("fetchEntityThumbnails");
    expect(source).toContain("entityCardToThumbnailCard");
    expect(source).toContain("thumbnailCardsById");
    expect(source).toContain("toAspectRatioNumeric");
    expect(source).not.toContain("apiAssetUrl");
    expect(source).not.toContain("function coverFor");
    expect(source).not.toContain("hoverPreviewsEnabled={false}");
    expect(source).not.toContain('aspectRatio: "square"');
    expect(source).not.toContain("placeholder-frame");
    expect(source).not.toContain("{#snippet entityArtwork");
  });
});
