import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

describe("audio track detail page source", () => {
  it("uses the shared detail thumbnail cover for player artwork", () => {
    const source = readFileSync("src/routes/audio/tracks/[id]/+page.svelte", "utf8");

    expect(source).toContain("const coverUrl = $derived(card?.posterCard?.cover?.src ?? card?.poster?.src ?? null);");
    expect(source).toContain("coverUrl,");
    expect(source).not.toContain("parentCoverUrl");
    expect(source).not.toContain("fetchEntityThumbnails");
  });
});
