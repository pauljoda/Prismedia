import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("EntityIndexPage header", () => {
  it("keeps index page headers to only an icon and title", async () => {
    const source = await readFile("src/lib/components/entities/EntityIndexPage.svelte", "utf8");

    expect(source).toContain("page-head-title");
    expect(source).toContain("page-head-icon");
    expect(source).not.toContain("page-head-eyebrow");
    expect(source).not.toContain("page-head-description");
    expect(source).not.toContain("LIBRARY ·");
    expect(source).not.toContain("description: string");
  });
});
