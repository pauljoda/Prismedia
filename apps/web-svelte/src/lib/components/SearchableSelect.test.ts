import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import { SearchableSelect } from "@prismedia/ui-svelte";

const labels = { label: "Catalog", searchLabel: "Search catalog" };

describe("SearchableSelect", () => {
  it("limits rendered choices without making later matches unreachable", async () => {
    const options = Array.from({ length: 60 }, (_, index) => ({ value: `item-${index}`, label: `Item ${index}` }));
    render(SearchableSelect, { ...labels, options, value: options[0].value, onchange: vi.fn() });
    await fireEvent.click(screen.getByRole("button", { name: "Catalog: Item 0" }));
    expect(screen.getAllByRole("option")).toHaveLength(50);
    await fireEvent.input(screen.getByRole("combobox", { name: labels.searchLabel }), { target: { value: "item-59" } });
    expect(screen.getAllByRole("option")).toHaveLength(1);
    expect(screen.getByRole("option", { name: "Item 59" })).toBeInTheDocument();
  });

  it("skips disabled choices when navigating with the keyboard", async () => {
    const onchange = vi.fn();
    render(SearchableSelect, { ...labels, options: [
      { value: "first", label: "First" },
      { value: "disabled", label: "Unavailable", disabled: true },
      { value: "last", label: "Last" },
    ], value: "first", onchange });
    await fireEvent.click(screen.getByRole("button", { name: "Catalog: First" }));
    const input = screen.getByRole("combobox", { name: labels.searchLabel });
    await waitFor(() => expect(input).toHaveFocus());
    await fireEvent.click(screen.getByRole("option", { name: "Unavailable" }));
    expect(onchange).not.toHaveBeenCalled();
    await fireEvent.keyDown(input, { key: "ArrowDown" });
    await waitFor(() => expect(screen.getByRole("option", { name: "Last" })).toHaveAttribute("aria-selected", "true"));
    await fireEvent.keyDown(input, { key: "Enter" });
    expect(onchange).toHaveBeenCalledExactlyOnceWith("last");
  });

  it("follows parent-owned values and supports a disabled trigger", async () => {
    const { rerender } = render(SearchableSelect, { ...labels, options: [
      { value: "first", label: "First" }, { value: "next", label: "Next" },
    ], value: "first", onchange: vi.fn() });
    expect(screen.getByRole("button", { name: "Catalog: First" })).toBeInTheDocument();
    await rerender({ value: "next", disabled: true });
    expect(screen.getByRole("button", { name: "Catalog: Next" })).toBeDisabled();
  });
});
