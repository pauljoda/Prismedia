import { cleanup, fireEvent, render, screen } from "@testing-library/svelte";
import { SearchInput } from "@prismedia/ui-svelte";
import { afterEach, describe, expect, it } from "vitest";

describe("SearchInput", () => {
  afterEach(cleanup);

  it("clears the current query and returns focus to the input", async () => {
    render(SearchInput, {
      value: "matrix",
      ariaLabel: "Search library",
      placeholder: "Search everything…",
    });

    const input = screen.getByRole("searchbox", { name: "Search library" }) as HTMLInputElement;
    await fireEvent.click(screen.getByRole("button", { name: "Clear search" }));
    await Promise.resolve();

    expect(input.value).toBe("");
    expect(document.activeElement).toBe(input);
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
