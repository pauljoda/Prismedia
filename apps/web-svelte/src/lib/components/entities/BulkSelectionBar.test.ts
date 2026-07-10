import { cleanup, fireEvent, render, screen } from "@testing-library/svelte";
import { afterEach, describe, expect, it, vi } from "vitest";
import BulkSelectionBar from "./BulkSelectionBar.svelte";

describe("BulkSelectionBar", () => {
  afterEach(cleanup);

  it("hides contextual actions that do not support the current selection", async () => {
    const run = vi.fn();
    const view = render(BulkSelectionBar, {
      bulkActions: [{
        id: "delete-files",
        label: "Delete files",
        isAvailable: (ids: string[]) => ids.every((id) => id === "on-disk"),
        onRun: run,
      }],
      onClearSelection: vi.fn(),
      onSelectAllVisible: vi.fn(),
      selectedCount: 1,
      selectedIds: ["wanted"],
      showSelectionToggle: false,
    });

    expect(screen.queryByRole("button", { name: "Bulk actions" })).toBeNull();

    await view.rerender({
      bulkActions: [{
        id: "delete-files",
        label: "Delete files",
        isAvailable: (ids: string[]) => ids.every((id) => id === "on-disk"),
        onRun: run,
      }],
      onClearSelection: vi.fn(),
      onSelectAllVisible: vi.fn(),
      selectedCount: 1,
      selectedIds: ["on-disk"],
      showSelectionToggle: false,
    });

    await fireEvent.click(screen.getByRole("button", { name: "Bulk actions" }));
    await fireEvent.click(screen.getByRole("button", { name: "Delete files" }));
    expect(run).toHaveBeenCalledWith(["on-disk"]);
  });
});
