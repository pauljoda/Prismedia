import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("collection playlist wiring", () => {
  it("uses ordered collection item membership for grids and shell playlist playback", async () => {
    const source = await readFile("src/routes/collections/[id]/+page.svelte", "utf8");

    expect(source).toContain("fetchCollectionItems");
    expect(source).toContain("playlist.startPlaylist(collectionItems, collection.title, 0");
    expect(source).toContain("slideshowDurationSeconds: slideshowDurationSeconds()");
    expect(source).toContain("aria-label=\"Play collection\"");
    expect(source).toContain("aria-label=\"Shuffle collection\"");
  });
});
