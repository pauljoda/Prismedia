import { fireEvent, render, screen, within } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import EntityGridToolbar from "./EntityGridToolbar.test-harness.svelte";

describe("EntityGridToolbar", () => {
  it("keeps search, view controls and selection in their original separate rows", async () => {
    render(EntityGridToolbar, { props: { canClearFilters: false } });
    const viewRow = screen.getByRole("radio", { name: "Grid view" }).closest(".controls-row");
    expect(viewRow).toContainElement(screen.getByRole("button", { name: "Filters" }));
    expect(viewRow).not.toContainElement(screen.getByRole("searchbox"));
    const selectionRow = screen.getByRole("status");
    const select = within(selectionRow).getByRole("button", { name: "Select" });
    expect(viewRow).not.toContainElement(select);
    await fireEvent.click(select);
    expect(within(selectionRow).getByText("0 selected")).toBeInTheDocument();
    await fireEvent.click(within(selectionRow).getByRole("button", { name: "Done" }));
    expect(within(selectionRow).queryByText("0 selected")).not.toBeInTheDocument();
  });

  it("keeps manual secondary-row collapse as the public persisted callback", async () => {
    const onBarsCollapsedChange = vi.fn();
    render(EntityGridToolbar, { props: { onBarsCollapsedChange } });

    await fireEvent.click(screen.getByRole("button", { name: "Hide filter and selection rows" }));
    expect(onBarsCollapsedChange).toHaveBeenCalledWith(true);
    expect(screen.getByRole("button", { name: "Show filter and selection rows" })).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Grid view" })).toBeInTheDocument();

    await fireEvent.click(screen.getByRole("button", { name: "Show filter and selection rows" }));
    expect(onBarsCollapsedChange).toHaveBeenLastCalledWith(false);
  });
});
