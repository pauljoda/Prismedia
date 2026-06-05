import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("ebooks index route", () => {
  it("keeps eBooks filtered to prose book types", async () => {
    const source = await readFile("src/routes/ebooks/+page.svelte", "utf8");

    expect(source).toContain('lockedServerQuery={{ bookType: "book,novel", bookFormat: "epub,pdf" }}');
    expect(source).toContain("lockBookFilters");
  });
});
