import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import { ENTITY_KIND } from "$lib/entities/entity-codes";
import { entityAccentForKind } from "$lib/entities/entity-accent";
import { ENTITY_GRID_ALL_KINDS } from "$lib/entities/entity-grid";
import EntityGridTabs from "./EntityGridTabs.svelte";

const tabs = [
  { kind: ENTITY_KIND.book, label: "Books", count: 7 },
  { kind: ENTITY_KIND.movie, label: "Movies", count: 2 },
];

describe("EntityGridTabs", () => {
  it("renders separated type filters with canonical colored icons and counts", () => {
    render(EntityGridTabs, { tabs, totalCount: 9, activeKind: ENTITY_GRID_ALL_KINDS, onActiveKindChange: vi.fn() });
    const group = screen.getByRole("group", { name: "Entity kinds" });
    expect(group).toHaveAttribute("data-spacing", "2");
    expect(group).toHaveClass("flex-wrap");
    expect(screen.getByRole("radio", { name: "All 9" })).toHaveAttribute("aria-checked", "true");
    for (const tab of tabs) {
      const filter = screen.getByRole("radio", { name: `${tab.label} ${tab.count}` });
      expect(filter.querySelector("svg")).toHaveAttribute("stroke", entityAccentForKind(tab.kind).primary);
    }
  });

  it("changes the kind without allowing the active filter to be deselected", async () => {
    const onActiveKindChange = vi.fn();
    render(EntityGridTabs, { tabs, totalCount: 9, activeKind: ENTITY_GRID_ALL_KINDS, onActiveKindChange });
    await fireEvent.click(screen.getByRole("radio", { name: "All 9" }));
    expect(onActiveKindChange).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("radio", { name: "Books 7" }));
    expect(onActiveKindChange).toHaveBeenCalledWith(ENTITY_KIND.book);
  });

  it("omits the row for a single entity kind", () => {
    render(EntityGridTabs, { tabs: tabs.slice(0, 1), totalCount: 7, activeKind: ENTITY_GRID_ALL_KINDS, onActiveKindChange: vi.fn() });
    expect(screen.queryByRole("group", { name: "Entity kinds" })).not.toBeInTheDocument();
  });
});
