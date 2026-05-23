import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("gallery detail lightbox wiring", () => {
  it("uses the same hydrated grid-page lightbox path as the image index", async () => {
    const source = await readFile("src/routes/galleries/[id]/+page.svelte", "utf8");

    expect(source).toContain("ImageLightboxDetails");
    expect(source).toContain("fetchImage");
    expect(source).toContain("hydrateLightboxEntity");
    expect(source).toContain("visibleCards: EntityThumbnailCard[]");
    expect(source).toContain("lightboxCards = nextCards");
    expect(source).toContain("{#snippet detailsContent(entity)}");
  });
});
