import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("videos index playback wiring", () => {
  it("enables feed and universal lightbox playback for video cards", async () => {
    const source = await readFile("src/routes/videos/+page.svelte", "utf8");
    const componentSource = await readFile("src/lib/components/entities/EntityIndexPage.svelte", "utf8");

    expect(source).toContain("enableFeedView");
    expect(source).toContain("enableLightbox");
    expect(source).toContain("lightboxTitle=\"Videos\"");
    expect(source).toContain("initialMediaWall");
    expect(componentSource).toContain("fetchVideo");
    expect(componentSource).toContain("lightboxEntityFromVideoDetail");
    expect(componentSource).toContain('currentCard.entity.kind !== "video"');
  });
});
