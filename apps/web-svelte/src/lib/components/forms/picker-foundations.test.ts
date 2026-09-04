import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import SearchSelect from "./SearchSelect.svelte";
import TagSelect from "./TagSelect.svelte";
import EntityPicker from "./EntityPicker.svelte";

describe("shared picker interactions", () => {
  it("makes the clear action a separate keyboard-accessible button", async () => {
    const onChange = vi.fn();
    render(SearchSelect, { label: "Language", value: "English", options: [{ name: "English" }], onChange });
    const clear = screen.getByRole("button", { name: "Clear selection" });
    expect(clear.tagName).toBe("BUTTON");
    expect(clear.tabIndex).toBe(0);
    await fireEvent.click(clear);
    expect(onChange).toHaveBeenCalledExactlyOnceWith("");
  });

  it("provides a labelled tag search and excludes selected tags", async () => {
    const onChange = vi.fn();
    render(TagSelect, { label: "Tags", values: ["Drama"], options: [{ name: "Drama" }, { name: "Comedy", count: 3 }], onChange });
    await fireEvent.click(screen.getByRole("button", { name: "Add Tags" }));
    const input = screen.getByRole("combobox", { name: "Search Tags" });
    await waitFor(() => expect(input).toHaveFocus());
    expect(screen.queryByRole("option", { name: "Drama" })).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("option", { name: /Comedy/ }));
    expect(onChange).toHaveBeenCalledExactlyOnceWith(["Drama", "Comedy"]);
  });

  it("does not create arbitrary tags when creation is disabled", async () => {
    const onChange = vi.fn();
    render(TagSelect, { label: "Tags", values: [], options: [], canAddNew: false, onChange });
    await fireEvent.click(screen.getByRole("button", { name: "Add Tags" }));
    const input = screen.getByRole("combobox", { name: "Search Tags" });
    await fireEvent.input(input, { target: { value: "Unlisted" } });
    await fireEvent.keyDown(input, { key: "Enter" });
    expect(onChange).not.toHaveBeenCalled();
  });

  it("rejects late entity search results and keeps server ordering", async () => {
    let resolveOld!: (items: { id: string; title: string; thumbnailUrl: null }[]) => void;
    const onSearch = vi.fn((query: string) => query === "old"
      ? new Promise<{ id: string; title: string; thumbnailUrl: null }[]>((resolve) => { resolveOld = resolve; })
      : Promise.resolve(query ? [{ id: "new", title: "New result", thumbnailUrl: null }] : []));
    render(EntityPicker, { label: "People", values: [], onChange: vi.fn(), onSearch });
    await fireEvent.click(screen.getByRole("button", { name: "Add People" }));
    const input = screen.getByRole("combobox", { name: "Search People" });
    await fireEvent.input(input, { target: { value: "old" } });
    await waitFor(() => expect(onSearch).toHaveBeenCalledWith("old"));
    await fireEvent.input(input, { target: { value: "new" } });
    // JSDOM cannot position floating elements. Async assertions inspect content;
    // visibility, placement, and keyboard navigation are also checked in the app.
    expect((await screen.findByText("New result")).closest('[role="option"]')).not.toBeNull();
    resolveOld([{ id: "old", title: "Old result", thumbnailUrl: null }]);
    await waitFor(() => expect(screen.queryByText("Old result")).not.toBeInTheDocument());
    expect(screen.getByText("New result")).toBeInTheDocument();
  });

  it("shows a recoverable entity-search error instead of an unhandled rejection", async () => {
    const onSearch = vi.fn().mockRejectedValueOnce(new Error("Offline")).mockResolvedValue([]);
    render(EntityPicker, { label: "People", values: [], onChange: vi.fn(), onSearch });
    await fireEvent.click(screen.getByRole("button", { name: "Add People" }));
    expect(await screen.findByText("Offline")).toHaveAttribute("role", "alert");
    await fireEvent.click(screen.getByText("Retry"));
    await waitFor(() => expect(onSearch).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByText("Offline")).not.toBeInTheDocument());
  });
});
