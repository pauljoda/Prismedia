import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

function readPageSource() {
  return readFileSync(resolve(process.cwd(), "src/routes/galleries/[id]/+page.svelte"), "utf8");
}

describe("/galleries/[id] navigation", () => {
  it("reloads gallery detail data when the route id changes inside the same route", () => {
    const pageSource = readPageSource();

    expect(pageSource).toContain('const currentGalleryId = $derived(page.params.id ?? "")');
    expect(pageSource).toContain("void loadGallery(currentGalleryId, currentNsfwMode)");
    expect(pageSource).not.toContain("onMount(() =>");
  });
});
