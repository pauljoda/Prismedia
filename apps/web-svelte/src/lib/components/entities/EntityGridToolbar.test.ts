import { render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import { CAPABILITY_KIND } from "$lib/entities/entity-codes";
import EntityGridToolbar from "./EntityGridToolbar.svelte";

describe("EntityGridToolbar", () => {
  it("keeps the reset action in the active filter row", () => {
    const { container } = render(EntityGridToolbar, {
      props: {
        activeFilterIds: ["progress:played:false"],
        activePresetId: null,
        allSelectedNsfw: false,
        bulkActions: [],
        canClearFiltersAndSort: true,
        drawerOpen: false,
        filterOptions: [
          {
            id: "progress:played:false",
            count: 1,
            label: "Unplayed",
            capabilityKind: CAPABILITY_KIND.progress,
            value: "played:false",
          },
        ],
        maxScale: 8,
        minScale: 2,
        onActiveFilterIdsChange: vi.fn(),
        onApplyPreset: vi.fn(),
        onClearFiltersAndSort: vi.fn(),
        onClearSelection: vi.fn(),
        onDeletePreset: vi.fn(),
        onDrawerOpenChange: vi.fn(),
        onSelectAllVisible: vi.fn(),
        onSelectionActiveChange: vi.fn(),
        onOverwritePreset: vi.fn(),
        onQueryChange: vi.fn(),
        onMediaWallChange: vi.fn(),
        onSavePreset: vi.fn(),
        onScaleChange: vi.fn(),
        onSortByChange: vi.fn(),
        onSortDirChange: vi.fn(),
        onToggleNsfwFlag: vi.fn(),
        onReshuffle: vi.fn(),
        onViewModeChange: vi.fn(),
        presets: [],
        mediaWall: false,
        query: "",
        scale: 4,
        selectable: true,
        selectedCount: 0,
        selectedIds: [],
        selectionActive: false,
        sortBy: "title",
        sortDir: "asc",
        viewMode: "grid",
      },
    });

    const clearButton = screen.getByRole("button", { name: "Clear" });

    expect(container.querySelector(".filter-row")?.contains(clearButton)).toBe(true);
    expect(container.querySelector(".controls-row")?.contains(clearButton)).toBe(false);
  });

  it("renders the selection bar inside the toolbar stack", () => {
    const { container } = render(EntityGridToolbar, {
      props: {
        activeFilterIds: [],
        activePresetId: null,
        allSelectedNsfw: false,
        bulkActions: [],
        canClearFiltersAndSort: false,
        drawerOpen: false,
        filterOptions: [],
        maxScale: 8,
        minScale: 2,
        onActiveFilterIdsChange: vi.fn(),
        onApplyPreset: vi.fn(),
        onClearFiltersAndSort: vi.fn(),
        onClearSelection: vi.fn(),
        onDeletePreset: vi.fn(),
        onDrawerOpenChange: vi.fn(),
        onSelectAllVisible: vi.fn(),
        onSelectionActiveChange: vi.fn(),
        onOverwritePreset: vi.fn(),
        onQueryChange: vi.fn(),
        onMediaWallChange: vi.fn(),
        onSavePreset: vi.fn(),
        onScaleChange: vi.fn(),
        onSortByChange: vi.fn(),
        onSortDirChange: vi.fn(),
        onToggleNsfwFlag: vi.fn(),
        onReshuffle: vi.fn(),
        onViewModeChange: vi.fn(),
        presets: [],
        mediaWall: false,
        query: "",
        scale: 4,
        selectable: true,
        selectedCount: 0,
        selectedIds: [],
        selectionActive: false,
        sortBy: "title",
        sortDir: "asc",
        viewMode: "grid",
      },
    });

    const selectButton = screen.getByRole("button", { name: "Select" });

    expect(container.querySelector(".toolbar-stack")?.contains(selectButton)).toBe(true);
    expect(container.querySelector(".bulk-bar")?.classList.contains("toolbar-bar")).toBe(true);
  });
});
