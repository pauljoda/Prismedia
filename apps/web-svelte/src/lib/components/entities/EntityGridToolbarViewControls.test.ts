import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import EntityGridToolbarViewControls from "./EntityGridToolbarViewControls.svelte";
import { ENTITY_GRID_VIEW_MODE } from "$lib/entities/entity-grid";

function props() {
  return { minScale: 2, maxScale: 12, scale: 6, mediaWall: false, viewMode: ENTITY_GRID_VIEW_MODE.grid,
    onMediaWallChange: vi.fn(), onScaleChange: vi.fn(), onViewModeChange: vi.fn() };
}

describe("library display options", () => {
  it("groups view, density and artwork controls in a named panel with focus return", async () => {
    const callbacks = props();
    render(EntityGridToolbarViewControls, callbacks);
    expect(screen.queryByRole("slider")).not.toBeInTheDocument();
    const trigger = screen.getByRole("button", { name: "Display options" });
    trigger.focus();
    await fireEvent.click(trigger);
    expect(await screen.findByRole("dialog", { name: "Display options" })).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("radio", { name: "List view" }));
    expect(callbacks.onViewModeChange).toHaveBeenCalledWith(ENTITY_GRID_VIEW_MODE.list);
    await fireEvent.click(screen.getByRole("switch", { name: "Artwork only" }));
    expect(callbacks.onMediaWallChange).toHaveBeenCalledWith(true);
    const slider = screen.getByRole("slider", { name: "Thumbnail columns" });
    slider.focus();
    await fireEvent.keyDown(slider, { key: "ArrowRight" });
    expect(callbacks.onScaleChange).toHaveBeenCalledWith(7);
    await fireEvent.keyDown(slider, { key: "Escape" });
    await waitFor(() => expect(trigger).toHaveFocus());
  });

  it("does not deselect the active view and hides artwork options in list mode", async () => {
    const callbacks = props();
    render(EntityGridToolbarViewControls, { ...callbacks, viewMode: ENTITY_GRID_VIEW_MODE.list, enableFeedView: true });
    await fireEvent.click(screen.getByRole("button", { name: "Display options" }));
    await fireEvent.click(screen.getByRole("radio", { name: "List view" }));
    expect(callbacks.onViewModeChange).not.toHaveBeenCalled();
    expect(screen.queryByRole("slider")).not.toBeInTheDocument();
    expect(screen.queryByRole("switch")).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("radio", { name: "Feed view" }));
    expect(callbacks.onViewModeChange).toHaveBeenCalledWith(ENTITY_GRID_VIEW_MODE.feed);
  });
});
