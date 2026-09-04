import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import EntityGridToolbar from "./EntityGridToolbar.test-harness.svelte";

describe("EntityGridToolbar", () => {
  it("only shows bulk controls during selection and expands them from a persisted collapsed state", async () => {
    render(EntityGridToolbar, { props: { barsCollapsed: true, canClearFiltersAndSort: false } });
    expect(screen.queryByText("0 selected")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Show filter and selection rows" })).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Select items" }));
    expect(screen.getByText("0 selected")).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Exit selection" }));
    expect(screen.getByRole("button", { name: "Select items" })).toHaveAttribute("aria-pressed", "false");
    // Svelte keeps the outgoing row mounted until its slide animation finishes.
    // JSDOM's animation stub cannot finish it, but the row must stop being interactive immediately.
    await waitFor(() => expect(screen.getByText("0 selected").closest('[role="status"]')).toHaveProperty("inert", true));
  });

  it("keeps manual secondary-row collapse as the public persisted callback", async () => {
    const onBarsCollapsedChange = vi.fn();
    render(EntityGridToolbar, { props: { onBarsCollapsedChange } });

    await fireEvent.click(screen.getByRole("button", { name: "Hide filter and selection rows" }));
    expect(onBarsCollapsedChange).toHaveBeenCalledWith(true);
    expect(screen.getByRole("button", { name: "Show filter and selection rows" })).toBeInTheDocument();

    await fireEvent.click(screen.getByRole("button", { name: "Show filter and selection rows" }));
    expect(onBarsCollapsedChange).toHaveBeenLastCalledWith(false);
  });
});
