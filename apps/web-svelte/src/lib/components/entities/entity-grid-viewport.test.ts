import { describe, expect, it } from "vitest";
import { computeContainedScrollHeight } from "./entity-grid-viewport.svelte";

describe("entity-grid viewport sizing", () => {
  it("fits the scrollable grid into the visible viewport below its actual top edge", () => {
    expect(computeContainedScrollHeight({ top: 430, viewportHeight: 960, bottomPadding: 24 })).toBe("506px");
  });

  it("keeps a usable minimum height when the grid starts low on the page", () => {
    expect(computeContainedScrollHeight({ top: 820, viewportHeight: 960, bottomPadding: 24, minHeight: 280 })).toBe("280px");
  });

  it("does not grow beyond the visible viewport after the outer page scrolls past the grid top", () => {
    expect(computeContainedScrollHeight({ top: -900, viewportHeight: 960, bottomPadding: 24 })).toBe("936px");
  });
});
