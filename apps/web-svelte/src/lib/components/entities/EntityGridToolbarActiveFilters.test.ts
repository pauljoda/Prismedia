import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import EntityGridToolbarActiveFilters from "./EntityGridToolbarActiveFilters.svelte";

describe("active filter toolbar", () => {
  it("uses the shared inline button layout for Clear and invokes the reset action", async () => {
    const onClearFiltersAndSort = vi.fn();
    render(EntityGridToolbarActiveFilters, {
      activeFilterIds: [], activeFilters: [], canClearFiltersAndSort: true,
      onActiveFilterIdsChange: vi.fn(), onClearFiltersAndSort,
    });

    const clear = screen.getByRole("button", { name: "Clear" });
    // This row is outside the hero; its icon/label alignment must not depend on hero CSS.
    expect(clear).toHaveClass("inline-flex", "items-center");
    expect(clear).not.toHaveClass("ctrl-btn");
    await fireEvent.click(clear);
    expect(onClearFiltersAndSort).toHaveBeenCalledOnce();
  });

  it("does not offer a reset when the library view has no changes", () => {
    render(EntityGridToolbarActiveFilters, {
      activeFilterIds: [], activeFilters: [], canClearFiltersAndSort: false,
      onActiveFilterIdsChange: vi.fn(), onClearFiltersAndSort: vi.fn(),
    });
    expect(screen.queryByRole("button", { name: "Clear" })).not.toBeInTheDocument();
  });
});
