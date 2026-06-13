import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("identify detail provider seek", () => {
  it("advances providers, waits for each result, and exposes Seek only beside the search provider picker", async () => {
    const source = await readFile("src/routes/identify/[entityId]/+page.svelte", "utf8");

    expect(source).toContain("import { providerSeekOrder } from \"$lib/components/identify/identify-provider-seek\";");
    expect(source).toContain("async function runSeek()");
    expect(source).toContain("const orderedProviderIds = providerSeekOrder(providerIds, activeProviderId);");
    expect(source).toContain("selectedProviderId = providerId;");
    expect(source).toContain("const result = await store.waitForIdentifyResult(entityId, providerId);");
    expect(source).toContain("store.reviewResolvedQueueItem(result);");
    expect(source).toContain("item.state === IDENTIFY_QUEUE_STATE.proposal");
    expect(source).toContain("item.state === IDENTIFY_QUEUE_STATE.search");
    expect(source.match(/onclick=\{runSeek\}/g)).toHaveLength(1);
    expect(source).not.toContain("showReviewSeekAction");
    expect(source).toContain("identify-search-provider");
    expect(source).toContain("Provider</span>");
    expect(source).toContain("for=\"identify-manual-query\"");
    expect(source).toContain("id=\"identify-manual-query\"");
    expect(source).toContain("w-full rounded-xs border border-border-default");
    expect(source).toContain("sm:w-24");
    expect(source).toContain("sm:grid-cols-[minmax(12rem,32rem)_auto]");
    expect(source).toMatch(
      /<IdentifyProviderSelect[\s\S]*?onChange=\{\(providerId\) => \(selectedProviderId = providerId\)\}[\s\S]*?<button[\s\S]*?class=\{seekButtonClass\}[\s\S]*?onclick=\{runSeek\}[\s\S]*?Seek/,
    );
  });
});
