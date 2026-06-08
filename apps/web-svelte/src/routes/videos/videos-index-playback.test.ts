import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("videos index playback wiring", () => {
  it("enables feed playback but links video cards to their detail page", async () => {
    const source = await readFile("src/routes/videos/+page.svelte", "utf8");

    // Feed view (with inline media-wall playback) stays on for the videos index.
    expect(source).toContain("enableFeedView");
    expect(source).toContain("initialMediaWall");

    // Opening a video from the library navigates to its EntityDetail page rather
    // than the universal lightbox (see the videos-lightbox revert).
    expect(source).not.toContain("enableLightbox");
    expect(source).not.toContain("lightboxTitle");
  });
});
