import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("image detail route layout", () => {
  it("uses the universal lightbox as the primary image detail surface", async () => {
    const source = await readFile("src/routes/images/[id]/+page.svelte", "utf8");

    expect(source).toContain("<UniversalLightbox");
    expect(source).not.toContain("class=\"image-surface\"");
    expect(source).not.toContain("Open image viewer");
  });

  it("passes EntityDetail as the lightbox details back page", async () => {
    const source = await readFile("src/routes/images/[id]/+page.svelte", "utf8");

    expect(source).toContain("{#snippet detailsContent()}");
    expect(source).toContain("<EntityDetail");
    expect(source).not.toContain("showHero={false}");
  });
});
