import { describe, expect, it, vi } from "vitest";
import { EntityGridToolbarCollapseController } from "./entity-grid-toolbar-collapse-controller.svelte";

describe("EntityGridToolbarCollapseController", () => {
  it("collapses untouched rows after a meaningful downward scroll without persisting it", () => {
    const onManualChange = vi.fn();
    const controller = new EntityGridToolbarCollapseController(false, onManualChange);
    const cleanup = controller.connectScroll();
    const scrollTarget = document.createElement("div");
    scrollTarget.scrollTop = 64;
    document.body.append(scrollTarget);

    scrollTarget.dispatchEvent(new Event("scroll", { bubbles: true }));

    expect(controller.barsCollapsed).toBe(true);
    expect(onManualChange).not.toHaveBeenCalled();

    cleanup();
    scrollTarget.remove();
  });

  it("pins manual choices so later scroll events cannot change them", () => {
    const onManualChange = vi.fn();
    const controller = new EntityGridToolbarCollapseController(false, onManualChange);
    const cleanup = controller.connectScroll();
    const scrollTarget = document.createElement("div");
    document.body.append(scrollTarget);

    controller.toggle();
    controller.toggle();
    scrollTarget.scrollTop = 96;
    scrollTarget.dispatchEvent(new Event("scroll", { bubbles: true }));

    expect(controller.barsCollapsed).toBe(false);
    expect(onManualChange).toHaveBeenNthCalledWith(1, true);
    expect(onManualChange).toHaveBeenNthCalledWith(2, false);

    cleanup();
    scrollTarget.remove();
  });
});
