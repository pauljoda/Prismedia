import { describe, expect, it, vi } from "vitest";
import {
  computeContainedScrollHeight,
  entityGridScrollContainerBottom,
  findEntityGridScrollAncestor,
} from "./entity-grid-viewport.svelte";

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

  it("uses the nearest scrolling ancestor's padded lower edge", () => {
    const scrollContainer = document.createElement("main");
    const nested = document.createElement("div");
    const grid = document.createElement("div");
    scrollContainer.append(nested);
    nested.append(grid);
    document.body.append(scrollContainer);

    Object.defineProperty(scrollContainer, "getBoundingClientRect", {
      value: () => ({ bottom: 900 }),
    });
    const getComputedStyleSpy = vi.spyOn(window, "getComputedStyle").mockImplementation((element) => ({
      overflowY: element === scrollContainer ? "auto" : "visible",
      paddingBottom: element === scrollContainer ? "32px" : "0px",
    }) as CSSStyleDeclaration);

    expect(findEntityGridScrollAncestor(grid)).toBe(scrollContainer);
    expect(entityGridScrollContainerBottom(grid)).toBe(868);

    getComputedStyleSpy.mockRestore();
    scrollContainer.remove();
  });
});
