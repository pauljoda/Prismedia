import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import { ENTITY_KIND, ENTITY_SORT_DIRECTION } from "$lib/api/generated/codes";
import EntityGridToolbarSearch from "./EntityGridToolbarSearch.svelte";
import { ENTITY_GRID_SORT } from "$lib/entities/entity-grid";

function props() {
  return { query: "", sortBy: ENTITY_GRID_SORT.added, sortDir: ENTITY_SORT_DIRECTION.descending,
    onQueryChange: vi.fn(), onReshuffle: vi.fn(), onSortByChange: vi.fn(), onSortDirChange: vi.fn() };
}

describe("library search and sorting", () => {
  it("distinguishes sort direction from a disclosure control", () => {
    render(EntityGridToolbarSearch, props());
    const direction = screen.getByRole("button", { name: "Sort descending; switch to ascending" });
    expect(direction).toHaveAttribute("title", "Descending order; switch to ascending");
    expect(direction.querySelector(".lucide-arrow-down-wide-narrow")).not.toBeNull();
    expect(direction.querySelector(".lucide-chevron-down")).toBeNull();
  });

  it("offers a named keyboard select and returns focus without changing the sort on Escape", async () => {
    const callbacks = props();
    render(EntityGridToolbarSearch, callbacks);
    const trigger = screen.getByRole("button", { name: "Sort by" });
    trigger.focus();
    await fireEvent.keyDown(trigger, { key: "ArrowDown" });
    expect(await screen.findByRole("option", { name: "Date added" })).toHaveAttribute("aria-selected", "true");
    await fireEvent.keyDown(document.activeElement!, { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("listbox")).not.toBeInTheDocument());
    await waitFor(() => expect(trigger).toHaveFocus());
    expect(callbacks.onSortByChange).not.toHaveBeenCalled();
    await fireEvent.keyDown(trigger, { key: "ArrowDown" });
    await fireEvent.pointerUp(await screen.findByRole("option", { name: "Title" }));
    expect(callbacks.onSortByChange).toHaveBeenCalledWith(ENTITY_GRID_SORT.title);
    await fireEvent.click(screen.getByRole("button", { name: "Sort descending; switch to ascending" }));
    expect(callbacks.onSortDirChange).toHaveBeenCalledWith(ENTITY_SORT_DIRECTION.ascending);
  });

  it("keeps taxonomy sorting, reshuffle and query clearing contracts", async () => {
    const callbacks = props();
    render(EntityGridToolbarSearch, { ...callbacks, query: "test", entityKind: ENTITY_KIND.tag, sortBy: ENTITY_GRID_SORT.random });
    await fireEvent.click(screen.getByRole("button", { name: "Reshuffle the random order" }));
    expect(callbacks.onReshuffle).toHaveBeenCalledOnce();
    await fireEvent.click(screen.getByRole("button", { name: "Clear search" }));
    expect(callbacks.onQueryChange).toHaveBeenCalledWith("");
    expect(screen.getByRole("searchbox")).toHaveFocus();
    const trigger = screen.getByRole("button", { name: "Sort by" });
    trigger.focus();
    await fireEvent.keyDown(trigger, { key: "ArrowDown" });
    expect(await screen.findByRole("option", { name: "References" })).toBeInTheDocument();
  });
});
