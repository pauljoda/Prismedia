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
    expect(source).toContain("const dailyActivityBuckets = $derived.by");
    expect(source).toContain("const selectedChartBucket = $derived.by");
    expect(source).toContain("{#each dailyActivityBuckets as bucket (bucket.date)}");
    expect(source).toContain("chartBucketShareWidth");
    expect(source).toContain("aria-pressed={selectedChartBucket?.date === bucket.date}");
    expect(source).toContain("onclick={() => selectChartBucket(bucket.date)}");
    expect(source).toContain("const DAILY_ACTIVITY_VISIBLE_ROW_LIMIT = 15;");
    expect(source).toContain("style:max-height={`${DAILY_ACTIVITY_LIST_MAX_HEIGHT_REM}rem`}");
    expect(source).toContain("space-y-2 overflow-y-auto pr-1");
    expect(source).not.toContain("h-28 items-end");
    expect(source).not.toContain("max-w-9 flex-col");
    expect(source).not.toContain("max-h-64");
  });

  it("keeps the header picker wells sized to their options", () => {
    expect(source).toContain('class="flex max-w-full flex-wrap gap-1.5 lg:justify-end"');
    expect(source).toContain('class="surface-well flex w-fit max-w-full flex-wrap gap-1 p-0.5"');
    expect(source).not.toContain('class="grid gap-2 sm:grid-cols-2"');
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
