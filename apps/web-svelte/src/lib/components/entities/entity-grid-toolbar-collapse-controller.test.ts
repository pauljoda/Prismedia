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

  it("expands untouched rows again when scrolled back to the top", () => {
    const now = vi.spyOn(performance, "now").mockReturnValue(1_000);
    const controller = new EntityGridToolbarCollapseController(false, vi.fn());
    const cleanup = controller.connectScroll();
    const scrollTarget = document.createElement("div");
    document.body.append(scrollTarget);
    const scrollTo = (top: number) => {
      scrollTarget.scrollTop = top;
      scrollTarget.dispatchEvent(new Event("scroll", { bubbles: true }));
    };

    scrollTo(400);
    expect(controller.barsCollapsed).toBe(true);

    // Layout shifts while the rows slide away must not flip the toolbar straight back.
    scrollTo(20);
    expect(controller.barsCollapsed).toBe(true);

    now.mockReturnValue(2_000);
    scrollTo(300);
    expect(controller.barsCollapsed).toBe(true);
    scrollTo(0);
    expect(controller.barsCollapsed).toBe(false);

    cleanup();
    scrollTarget.remove();
    now.mockRestore();
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
