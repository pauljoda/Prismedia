import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("books index route", () => {
  it("keeps Books as the unfiltered page-based library view", async () => {
    const source = await readFile("src/routes/books/+page.svelte", "utf8");

    expect(source).not.toContain("lockedServerQuery");
    expect(source).not.toContain("lockBookFilters");
  });
});
