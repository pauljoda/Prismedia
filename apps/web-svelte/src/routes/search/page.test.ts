import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { page } from "$app/state";
import { ENTITY_KIND } from "$lib/entities/entity-codes";
import type { SearchResponse } from "$lib/search/models";
import SearchPage from "./+page.svelte";

const mocks = vi.hoisted(() => ({ search: vi.fn(), more: vi.fn(), goto: vi.fn() }));
vi.mock("$app/navigation", () => ({ goto: mocks.goto }));
vi.mock("$lib/nsfw/store.svelte", () => ({ useNsfw: () => ({ mode: "off" }) }));
vi.mock("$lib/search/entity-search", async importOriginal => ({
  ...await importOriginal<typeof import("$lib/search/entity-search")>(), searchEntities: mocks.search, loadMoreSearchResults: mocks.more,
}));

function response(): SearchResponse {
  const items = Array.from({ length: 25 }, (_, i) => ({
    id: `book-${i}`, kind: ENTITY_KIND.book, title: `Result ${i + 1}`, subtitle: "Books",
    imagePath: null, href: `/books/book-${i}`, rating: i < 5 ? 5 : 1, score: 1, meta: {},
  }));
  return { query: "story", durationMs: 1, groups: [{ kind: ENTITY_KIND.book, label: "Books", items, total: 25, hasMore: false }] };
}

describe("search results page", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    page.url = new URL("http://localhost/search?q=story") as typeof page.url;
    mocks.search.mockResolvedValue(response());
  });

  it("reveals the remaining fetched results without another request", async () => {
    render(SearchPage);
    await screen.findByRole("link", { name: "Result 1" });
    expect(screen.queryByRole("link", { name: "Result 25" })).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: /Show more/ }));
    expect(await screen.findByRole("link", { name: "Result 25" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Show more/ })).not.toBeInTheDocument();
    expect(mocks.search).toHaveBeenCalledOnce();
  });

  it("shows a retry action on failure instead of a blank results area", async () => {
    mocks.search.mockRejectedValueOnce(new Error("Offline"));
    render(SearchPage);
    await fireEvent.click(await screen.findByRole("button", { name: "Try again" }));
    expect(await screen.findByRole("link", { name: "Result 1" })).toBeInTheDocument();
    expect(mocks.search).toHaveBeenCalledTimes(2);
  });

  it("uses the shared rating picker to filter cached results", async () => {
    render(SearchPage);
    await screen.findByRole("link", { name: "Result 6" });
    await fireEvent.click(screen.getByRole("button", { name: "Filters" }));
    await fireEvent.click(screen.getByRole("button", { name: "Minimum 5 star rating" }));
    await waitFor(() => expect(screen.queryByRole("link", { name: "Result 6" })).not.toBeInTheDocument());
    expect(screen.getByRole("link", { name: "Result 5" })).toBeInTheDocument();
    expect(mocks.search).toHaveBeenCalledOnce();
    expect(screen.queryByLabelText("Date From")).not.toBeInTheDocument();
  });

  it("does not restore old results after the query is cleared", async () => {
    let complete!: (value: SearchResponse) => void;
    mocks.search.mockReturnValueOnce(new Promise<SearchResponse>(resolve => { complete = resolve; }));
    render(SearchPage);
    await waitFor(() => expect(mocks.search).toHaveBeenCalledOnce());
    await fireEvent.input(screen.getByRole("searchbox", { name: "Search everything" }), { target: { value: "" } });
    complete(response());
    await screen.findByText("Search your library");
    expect(screen.queryByRole("link", { name: "Result 1" })).not.toBeInTheDocument();
  });

  it("keeps full thumbnails without repeating the type label under every title", async () => {
    render(SearchPage);
    const result = await screen.findByRole("link", { name: "Result 1" });
    expect(result).not.toHaveTextContent("Books");
    expect(result).toHaveClass("entity-thumbnail");
    expect(screen.getByRole("link", { name: "Browse all books" })).toHaveAttribute("href", "/books");
  });

  it("offers server continuation even when the loaded batch has no matching kinds", async () => {
    const initial = response();
    initial.groups = [];
    initial.continuation = { requests: [{ params: { query: "story", cursor: "next" } }], expandedSourceIds: [], kinds: [ENTITY_KIND.book], includeRelated: false, relatedLimit: 20, batchSize: 1 };
    mocks.search.mockResolvedValueOnce(initial);
    mocks.more.mockResolvedValueOnce(response());
    render(SearchPage);
    await fireEvent.click(await screen.findByRole("button", { name: "Load more matches" }));
    expect(await screen.findByRole("link", { name: "Result 1" })).toBeInTheDocument();
    expect(mocks.more).toHaveBeenCalledOnce();
    expect(screen.queryByRole("button", { name: "Load more matches" })).not.toBeInTheDocument();
  });

  it("retains matches and offers a retry after a continuation failure", async () => {
    const initial = response();
    initial.continuation = { requests: [{ params: { query: "story", cursor: "next" } }], expandedSourceIds: [], kinds: [ENTITY_KIND.book], includeRelated: false, relatedLimit: 20, batchSize: 1 };
    mocks.search.mockResolvedValueOnce(initial);
    mocks.more.mockResolvedValueOnce({ ...initial, partialFailure: true });
    render(SearchPage);
    await fireEvent.click(await screen.findByRole("button", { name: "Load more matches" }));
    expect(await screen.findByRole("button", { name: "Retry remaining matches" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Result 1" })).toBeInTheDocument();
  });

  it("ignores an old continuation after starting another search", async () => {
    const initial = response();
    initial.continuation = { requests: [{ params: { query: "story", cursor: "next" } }], expandedSourceIds: [], kinds: [ENTITY_KIND.book], includeRelated: false, relatedLimit: 20, batchSize: 1 };
    mocks.search.mockResolvedValueOnce(initial);
    let finishMore!: (value: SearchResponse) => void;
    mocks.more.mockReturnValueOnce(new Promise<SearchResponse>(resolve => { finishMore = resolve; }));
    render(SearchPage);
    await fireEvent.click(await screen.findByRole("button", { name: "Load more matches" }));
    const next = response();
    next.query = "next";
    next.groups[0].items = [{ ...next.groups[0].items[0], title: "New search result" }];
    mocks.search.mockResolvedValueOnce(next);
    await fireEvent.input(screen.getByRole("searchbox", { name: "Search everything" }), { target: { value: "next" } });
    await screen.findByRole("link", { name: "New search result" });
    finishMore(response());
    await waitFor(() => expect(screen.getByRole("link", { name: "New search result" })).toBeInTheDocument());
    expect(screen.queryByRole("link", { name: "Result 1" })).not.toBeInTheDocument();
  });
});
