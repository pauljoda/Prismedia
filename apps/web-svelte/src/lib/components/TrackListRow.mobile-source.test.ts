import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

describe("TrackListRow mobile layout", () => {
  it("uses a mobile card grid before switching back to desktop columns", () => {
    const source = readFileSync("src/lib/components/TrackListRow.svelte", "utf8");

    expect(source).toContain('grid-template-areas:\n      "select index title  title  actions"');
    expect(source).toContain('grid-template-areas: "select index title rating time actions"');
    expect(source).toContain("@media (min-width: 640px)");
  });
});
