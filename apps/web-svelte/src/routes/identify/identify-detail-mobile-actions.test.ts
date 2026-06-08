import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("identify detail mobile actions", () => {
  it("keeps Back to Search visible on mobile proposal reviews and desktop without a search loader", async () => {
    const source = await readFile("src/routes/identify/[entityId]/+page.svelte", "utf8");

    expect(source).toContain("const backToSearchDisabled = $derived(isIdentifyingCurrent);");
    expect(source).not.toContain("const backToSearchBusy");
    expect(source).not.toContain("searching = true;\n    try {\n      await store.backToSearch");
    expect(source).toMatch(
      /class="[^"]*inline-flex[^"]*h-10[^"]*w-full[^"]*md:hidden[^"]*"[\s\S]*?disabled=\{backToSearchDisabled\}[\s\S]*?onclick=\{backToSearch\}[\s\S]*?<Search class="h-3\.5 w-3\.5" \/>[\s\S]*?Back to Search/,
    );
    expect(source).toMatch(
      /class="[^"]*hidden[^"]*md:inline-flex[^"]*"[\s\S]*?disabled=\{backToSearchDisabled\}[\s\S]*?onclick=\{backToSearch\}[\s\S]*?<Search class="h-3\.5 w-3\.5" \/>[\s\S]*?Back to Search/,
    );
  });
});
