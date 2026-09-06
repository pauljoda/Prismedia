import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { tick } from "svelte";
import { ENTITY_KIND } from "$lib/entities/entity-codes";
import type { SearchResponse } from "$lib/search/models";
import CommandPalette from "./CommandPalette.svelte";

const mocks = vi.hoisted(() => ({
  close: vi.fn(), goto: vi.fn(), search: vi.fn(), add: vi.fn(), remove: vi.fn(), clear: vi.fn(),
}));
vi.mock("$app/navigation", () => ({ goto: mocks.goto }));
vi.mock("$lib/stores/search.svelte", () => ({ useSearch: () => ({ open: true, closePalette: mocks.close }) }));
vi.mock("$lib/nsfw/store.svelte", () => ({ useNsfw: () => ({ mode: "off" }) }));
vi.mock("$lib/stores/recent-searches.svelte", () => ({
  recentSearches: () => ({ value: ["Previous search"], add: mocks.add, remove: mocks.remove, clear: mocks.clear }),
}));
vi.mock("$lib/search/entity-search", () => ({ searchEntities: mocks.search }));

function response(query = "film"): SearchResponse {
  return {
    query, durationMs: 10,
    groups: [{
      kind: ENTITY_KIND.movie, label: "Movies", total: 5, hasMore: true,
      items: ["A related result", "Second result", "Third result", "Fourth result"].map((title, index) => ({
        id: `movie-${index}`, kind: ENTITY_KIND.movie, title, subtitle: "2026",
        imagePath: null, href: `/movies/movie-${index}`, rating: null, score: 10 - index, meta: {},
      })),
    }],
  };
}

describe("global search palette", () => {
  beforeAll(() => {
    HTMLDialogElement.prototype.showModal = function () {
      this.open = true;
    };
    HTMLDialogElement.prototype.close = function () { this.open = false; };
  });
  beforeEach(() => { vi.clearAllMocks(); mocks.search.mockResolvedValue(response()); });
  afterEach(cleanup);

  it("provides a named search combobox and keyboard-selectable recent searches", async () => {
    render(CommandPalette);
    expect(screen.getByRole("dialog", { name: "Search library" })).toBeInTheDocument();
    const input = screen.getByRole("combobox", { name: "Search library" });
    await waitFor(() => expect(input).toHaveFocus());
    await fireEvent.keyDown(input, { key: "ArrowDown" });
    await fireEvent.keyDown(input, { key: "Enter" });
    expect(input).toHaveValue("Previous search");
  });

  it("keeps server-ranked results and includes see-all actions in keyboard navigation", async () => {
    render(CommandPalette);
    const input = screen.getByRole("combobox", { name: "Search library" });
    await fireEvent.input(input, { target: { value: "film" } });
    await screen.findByRole("option", { name: /A related result/ });
    expect(screen.queryByRole("option", { name: /Fourth result/ })).not.toBeInTheDocument();
    expect(mocks.search).toHaveBeenCalledWith(expect.objectContaining({ query: "film", hideNsfw: true }));
    await waitFor(() => expect(input.getAttribute("aria-activedescendant")).toBe(screen.getByRole("option", { name: /A related result/ }).id));
    for (let index = 0; index < 3; index++) await fireEvent.keyDown(input, { key: "ArrowDown" });
    await fireEvent.keyDown(input, { key: "Enter" });
    expect(mocks.goto).toHaveBeenCalledWith(`/search?q=film&kinds=${ENTITY_KIND.movie}`);
    expect(mocks.close).toHaveBeenCalledOnce();
  });

  it("clears pending results immediately and ignores an older search response", async () => {
    let finish!: (value: SearchResponse) => void;
    mocks.search.mockImplementationOnce(() => new Promise<SearchResponse>((resolve) => { finish = resolve; }));
    render(CommandPalette);
    const input = screen.getByRole("combobox", { name: "Search library" });
    await fireEvent.input(input, { target: { value: "film" } });
    await waitFor(() => expect(mocks.search).toHaveBeenCalledOnce());
    await fireEvent.input(input, { target: { value: "other" } });
    finish(response("film"));
    await tick();
    expect(screen.queryByRole("option", { name: /A related result/ })).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Clear search" }));
    expect(input).toHaveValue("");
    expect(input).toHaveFocus();
  });

  it("distinguishes a failed request from an empty result and allows retry", async () => {
    mocks.search.mockRejectedValueOnce(new Error("offline"));
    render(CommandPalette);
    await fireEvent.input(screen.getByRole("combobox", { name: "Search library" }), { target: { value: "film" } });
    expect(await screen.findByRole("alert")).toHaveTextContent("Search couldn't load");
    await fireEvent.click(screen.getByRole("button", { name: "Retry search" }));
    expect(await screen.findByRole("option", { name: /A related result/ })).toBeInTheDocument();
  });

  it("opens the first result with Enter and records the trimmed query", async () => {
    render(CommandPalette);
    const input = screen.getByRole("combobox", { name: "Search library" });
    await fireEvent.input(input, { target: { value: " film " } });
    await screen.findByRole("option", { name: /A related result/ });
    await fireEvent.keyDown(input, { key: "Enter" });
    expect(mocks.goto).toHaveBeenCalledWith("/movies/movie-0?from=%2F");
    expect(mocks.add).toHaveBeenCalledWith("film");
  });

  it("keeps the full-search action available when there are no results", async () => {
    mocks.search.mockResolvedValue({ query: "missing", groups: [], durationMs: 1 });
    render(CommandPalette);
    const input = screen.getByRole("combobox", { name: "Search library" });
    await fireEvent.input(input, { target: { value: "missing" } });
    expect(await screen.findByText("No results for “missing”")).toBeInTheDocument();
    await fireEvent.keyDown(input, { key: "Enter" });
    expect(mocks.goto).toHaveBeenCalledWith("/search?q=missing");
  });

  it("does not activate a result when a separate clear button handles Enter", async () => {
    render(CommandPalette);
    const input = screen.getByRole("combobox", { name: "Search library" });
    await fireEvent.input(input, { target: { value: "film" } });
    await screen.findByRole("option", { name: /A related result/ });
    const clear = screen.getByRole("button", { name: "Clear search" });
    clear.focus();
    await fireEvent.keyDown(clear, { key: "Enter" });
    expect(mocks.goto).not.toHaveBeenCalled();
  });
});
