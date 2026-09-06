import { fireEvent, render, screen, waitFor, within } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import EntityGridToolbarViewControls from "./EntityGridToolbarViewControls.svelte";
import { ENTITY_GRID_VIEW_MODE } from "$lib/entities/entity-grid";

function props() {
  return {
    minScale: 2, maxScale: 12, scale: 6, mediaWall: false,
    viewMode: ENTITY_GRID_VIEW_MODE.grid,
    onMediaWallChange: vi.fn(), onScaleChange: vi.fn(), onViewModeChange: vi.fn(),
  };
}

describe("library view controls", () => {
  it("keeps view, media wall and density directly available without a Display panel", async () => {
    const callbacks = props();
    render(EntityGridToolbarViewControls, callbacks);
    expect(screen.queryByRole("button", { name: "Display options" })).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("radio", { name: "List view" }));
    expect(callbacks.onViewModeChange).toHaveBeenCalledWith(ENTITY_GRID_VIEW_MODE.list);
    await fireEvent.click(screen.getByRole("button", { name: "Media wall" }));
    expect(callbacks.onMediaWallChange).toHaveBeenCalledWith(true);
    const slider = screen.getByRole("slider", { name: "Thumbnail columns" });
    slider.focus();
    await fireEvent.keyDown(slider, { key: "ArrowRight" });
    expect(callbacks.onScaleChange).toHaveBeenCalledWith(7);
  });

  it("keeps the compact thumbnail popover accessible with Escape and focus return", async () => {
    render(EntityGridToolbarViewControls, props());
    const trigger = screen.getByRole("button", { name: "Thumbnail size" });
    trigger.focus();
    await fireEvent.click(trigger);
    const panel = await screen.findByRole("dialog", { name: "Thumbnail size" });
    const slider = within(panel).getByRole("slider", { name: "Thumbnail columns" });
    slider.focus();
    await fireEvent.keyDown(slider, { key: "Escape" });
    await waitFor(() => expect(trigger).toHaveFocus());
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("does not deselect the active view and hides artwork controls in list mode", async () => {
    const callbacks = props();
    render(EntityGridToolbarViewControls, { ...callbacks, viewMode: ENTITY_GRID_VIEW_MODE.list, enableFeedView: true });
    await fireEvent.click(screen.getByRole("radio", { name: "List view" }));
    expect(callbacks.onViewModeChange).not.toHaveBeenCalled();
    expect(screen.getByRole("radio", { name: "List view" })).toHaveAttribute("aria-checked", "true");
    expect(screen.queryByRole("slider")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Media wall" })).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("radio", { name: "Feed view" }));
    expect(callbacks.onViewModeChange).toHaveBeenCalledWith(ENTITY_GRID_VIEW_MODE.feed);
  });
});
