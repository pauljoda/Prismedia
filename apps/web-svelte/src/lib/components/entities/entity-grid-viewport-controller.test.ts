import { describe, expect, it, vi } from "vitest";
import { EntityGridViewportController } from "./entity-grid-viewport-controller.svelte";

function buildController(scrollMaxHeight: string | null | undefined = undefined) {
  return new EntityGridViewportController({
    dockControls: () => true,
    scrollBottomPadding: () => 24,
    scrollMaxHeight: () => scrollMaxHeight,
    scrollMinHeight: () => 320,
  });
}

describe("EntityGridViewportController", () => {
  it("prefers an explicit viewport height and preserves the null opt-out", () => {
    expect(buildController("40rem").effectiveScrollMaxHeight).toBe("40rem");
    expect(buildController(null).effectiveScrollMaxHeight).toBeNull();
    expect(buildController(null).containsScroll).toBe(false);
  });

  it("suppresses hover previews briefly after scrolling", () => {
    const now = vi.spyOn(performance, "now");
    now.mockReturnValueOnce(100);
    const controller = buildController();
    controller.markScrolling();

    now.mockReturnValueOnce(319);
    expect(controller.areHoverPreviewsSuppressed()).toBe(true);
    now.mockReturnValueOnce(321);
    expect(controller.areHoverPreviewsSuppressed()).toBe(false);
  });
});
