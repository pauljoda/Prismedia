import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("images index lightbox wiring", () => {
  it("opens grid image cards in the universal lightbox with the current view", async () => {
    const source = await readFile("src/routes/images/+page.svelte", "utf8");
    const componentSource = await readFile("src/lib/components/entities/EntityIndexPage.svelte", "utf8");

    expect(source).toContain("enableLightbox");
    expect(source).toContain("lightboxTitle=\"Images\"");
    expect(componentSource).toContain("ImageLightboxDetails");
    expect(componentSource).toContain("{#snippet detailsContent(entity)}");
    expect(componentSource).toContain("fetchImage");
    expect(componentSource).toContain("hydrateLightboxEntity");
  });
});
