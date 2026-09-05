import { describe, expect, it } from "vitest";
import source from "./MetadataCardGrid.svelte?raw";

describe("MetadataCardGrid layout contract", () => {
  it("lets each row use the available width without empty reserved columns", () => {
    expect(source).toContain("display: flex");
    expect(source).toContain("flex-wrap: wrap");
    expect(source).toContain("flex: 1 1 var(--spacing-metadata-card-min)");
    expect(source).toContain("flex-grow: 2");
    expect(source).not.toContain("grid-column");
    expect(source).not.toContain("auto-fill");
  });

  it("keeps non-card sections full width and preserves source order", () => {
    expect(source).toMatch(/:not\(\.metadata-card\)\)\s*\{\s*flex: 1 1 100%/);
    expect(source).not.toMatch(/\border\s*:/);
  });
});
