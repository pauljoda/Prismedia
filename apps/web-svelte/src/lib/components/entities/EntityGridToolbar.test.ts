import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import EntityGridToolbar from "./EntityGridToolbar.test-harness.svelte";

describe("EntityGridToolbar", () => {
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
