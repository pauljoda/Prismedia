import { cleanup, fireEvent, render, screen } from "@testing-library/svelte";
import { SearchInput } from "@prismedia/ui-svelte";
import { afterEach, describe, expect, it, vi } from "vitest";

describe("SearchInput", () => {
  afterEach(cleanup);

  it("clears the current query and returns focus to the input", async () => {
    const onClear = vi.fn();

    render(SearchInput, {
      value: "matrix",
      ariaLabel: "Search library",
      placeholder: "Search everything…",
      onClear,
    });

    const input = screen.getByRole("searchbox", { name: "Search library" }) as HTMLInputElement;
    const clearButton = screen.getByRole("button", { name: "Clear search" });

    expect(clearButton).toHaveAttribute("title", "Clear search");
    expect(input).toHaveAttribute("data-slot", "input-group-control");
    expect(input).toHaveAttribute("autocomplete", "off");
    expect(clearButton).toHaveAttribute("data-slot", "input-group-button");

    await fireEvent.click(clearButton);
    await Promise.resolve();

    expect(input.value).toBe("");
    expect(document.activeElement).toBe(input);
    expect(onClear).toHaveBeenCalledOnce();
    expect(screen.queryByRole("button", { name: "Clear search" })).not.toBeInTheDocument();
    await fireEvent.input(input, { target: { value: "second query" } });
    await fireEvent.click(screen.getByRole("button", { name: "Clear search" }));
    expect(input).toHaveValue("");
    expect(onClear).toHaveBeenCalledTimes(2);
  });

  it("keeps loading state visible without replacing the editable query", () => {
    render(SearchInput, {
      value: "alien",
      ariaLabel: "Search library",
      loading: true,
    });

    expect(screen.getByRole("searchbox", { name: "Search library" })).toHaveValue("alien");
    expect(screen.getByLabelText("Searching")).toBeInTheDocument();
  });
});
