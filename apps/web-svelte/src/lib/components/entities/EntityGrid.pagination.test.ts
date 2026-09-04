import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
import { createEntityGridPrefs, type EntityGridPrefs } from "$lib/entities/entity-grid-prefs";
import EntityGrid from "./EntityGrid.test-harness.svelte";

// These tests assert grid state, not slide timing; the pagination RAF stub does not advance time.
vi.mock("svelte/transition", async (importOriginal) => ({
  ...await importOriginal<typeof import("svelte/transition")>(),
  slide: () => ({ duration: 0 }),
}));

const GRID_PREFS_DEFAULTS = {
  sortBy: "title",
  sortDir: "asc",
  mediaWall: false,
  scale: 11,
  pageSize: 100,
} as const;

/** Seeds this grid's persisted view state with a partial override for the test. */
function seedGridPrefs(prefsKey: string, prefs: Partial<EntityGridPrefs>): void {
  const store = createEntityGridPrefs(prefsKey, GRID_PREFS_DEFAULTS);
  store.save({ ...store.defaults(), ...prefs });
}

/** Reads back this grid's persisted view state. */
function readGridPrefs(prefsKey: string): EntityGridPrefs | null {
  return createEntityGridPrefs(prefsKey, GRID_PREFS_DEFAULTS).load();
}

describe("EntityGrid pagination", () => {
  beforeEach(() => {
    Object.defineProperty(window, "localStorage", {
      configurable: true,
      value: createLocalStorageStub(),
    });
    vi.stubGlobal("requestAnimationFrame", vi.fn((callback: FrameRequestCallback) => {
      callback(0);
      return 1;
    }));
    vi.stubGlobal("cancelAnimationFrame", vi.fn());
    vi.stubGlobal("ResizeObserver", class {
      observe = vi.fn();
      unobserve = vi.fn();
      disconnect = vi.fn();
    });
    Object.defineProperty(HTMLElement.prototype, "scrollTo", {
      configurable: true,
      value: vi.fn(),
    });
    Object.defineProperty(window, "scrollTo", {
      configurable: true,
      value: vi.fn(),
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("defaults desktop thumbnail grids to the preferred mid-range card size when no saved size exists", async () => {
    vi.stubGlobal("matchMedia", createMatchMedia(false));
    const cards = Array.from({ length: 6 }, (_, index) => card(index));
    const { container } = render(EntityGrid, {
      props: {
        cards,
        prefsKey: "desktop-small-default-test",
      },
    });

    await waitFor(() => {
      expect(container.querySelector<HTMLElement>(".entity-grid")?.style.getPropertyValue("--col-count")).toBe("6");
    });
  });

  it("defaults mobile thumbnail grids to the second-from-largest card size when no saved size exists", async () => {
    vi.stubGlobal("matchMedia", createMatchMedia(true));
    const cards = Array.from({ length: 6 }, (_, index) => card(index));
    const { container } = render(EntityGrid, {
      props: {
        cards,
        prefsKey: "mobile-largest-default-test",
      },
    });

    // minScale (2) is the largest size; the mobile default starts one step in.
    await waitFor(() => {
      expect(container.querySelector<HTMLElement>(".entity-grid")?.style.getPropertyValue("--col-count")).toBe("3");
    });
  });

  it("keeps saved thumbnail size preferences ahead of the mobile default", async () => {
    vi.stubGlobal("matchMedia", createMatchMedia(true));
    seedGridPrefs("mobile-saved-size-test", { scale: 7 });
    const cards = Array.from({ length: 6 }, (_, index) => card(index));
    const { container } = render(EntityGrid, {
      props: {
        cards,
        prefsKey: "mobile-saved-size-test",
      },
    });

    await waitFor(() => {
      expect(container.querySelector<HTMLElement>(".entity-grid")?.style.getPropertyValue("--col-count")).toBe("7");
    });
  });

  it("renders the default page instead of every card in a large grid", async () => {
    const cards = Array.from({ length: 4_500 }, (_, index) => card(index));
    let renderedCount = 0;

    const { container } = render(EntityGrid, {
      props: {
        cards,
        onRenderedCountChange: (count) => {
          renderedCount = count;
        },
        prefsKey: undefined,
      },
    });

    await waitFor(() => {
      expect(renderedCount).toBe(100);
      expect(container.querySelectorAll(".entity-thumbnail").length).toBe(100);
      expect(screen.getByText("Page 1 / 45")).toBeInTheDocument();
    });
  });

  it("lets the docked pagination bar choose page size and move pages", async () => {
    const cards = Array.from({ length: 150 }, (_, index) => card(index));
    const { container } = render(EntityGrid, {
      props: {
        cards,
        prefsKey: undefined,
      },
    });

    await fireEvent.click(screen.getByLabelText("Per page"));
    await fireEvent.click(screen.getByRole("button", { name: "100" }));

    await waitFor(() => {
      expect(container.querySelectorAll(".entity-thumbnail").length).toBe(100);
      expect(screen.getByText("Page 1 / 2")).toBeInTheDocument();
    });

    await fireEvent.click(screen.getByLabelText("Next page"));

    await waitFor(() => {
      expect(screen.getByText("101–150")).toBeInTheDocument();
      expect(screen.getByText("Page 2 / 2")).toBeInTheDocument();
    });
  });

  it("omits pagination chrome for grids that do not exceed the smallest page size", async () => {
    const cards = Array.from({ length: 100 }, (_, index) => card(index));
    const { container } = render(EntityGrid, {
      props: {
        cards,
        prefsKey: undefined,
      },
    });

    await waitFor(() => {
      expect(container.querySelectorAll(".entity-thumbnail")).toHaveLength(100);
      expect(container.querySelector(".pagination-shell")).toBeNull();
      expect(screen.queryByText("Page 1 / 1")).not.toBeInTheDocument();
    });
  });

  it("keeps page-size controls for grids that exceed the smallest page size", async () => {
    const cards = Array.from({ length: 150 }, (_, index) => card(index));
    const { container } = render(EntityGrid, {
      props: {
        cards,
        prefsKey: undefined,
      },
    });

    await waitFor(() => {
      expect(container.querySelectorAll(".entity-thumbnail")).toHaveLength(100);
      expect(container.querySelector(".pagination-shell")).not.toBeNull();
      expect(screen.getByLabelText("Per page")).toHaveTextContent("100");
      expect(screen.getByText("Page 1 / 2")).toBeInTheDocument();
    });
  });

  it("passes the current rendered page to card activation handlers", async () => {
    const cards = Array.from({ length: 300 }, (_, index) => card(index));
    const onCardActivate = vi.fn();
    render(EntityGrid, {
      props: {
        cards,
        onCardActivate,
        prefsKey: undefined,
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Video 0005" }));

    expect(onCardActivate).toHaveBeenCalledWith(
      cards[5],
      expect.arrayContaining([cards[0], cards[99]]),
    );
    expect(onCardActivate.mock.calls[0][1]).toHaveLength(100);
  });

  it("keeps the current page and card order when secondary toolbar rows expand", async () => {
    const onRequestChange = vi.fn();
    const { container } = render(EntityGrid, {
      cards: Array.from({ length: 150 }, (_, index) => card(index)),
      onRequestChange,
      prefsKey: "toolbar-expansion-test",
    });
    await fireEvent.click(screen.getByLabelText("Next page"));
    await waitFor(() => expect(screen.getByText("Page 2 / 2")).toBeInTheDocument());
    const titles = () => Array.from(container.querySelectorAll(".entity-thumbnail h3")).map((el) => el.textContent);
    const originalTitles = titles();
    expect(originalTitles).toHaveLength(50);
    const originalRequests = onRequestChange.mock.calls.length;

    await fireEvent.click(screen.getByRole("button", { name: "Hide filter and selection rows" }));
    expect(readGridPrefs("toolbar-expansion-test")?.barsCollapsed).toBe(true);
    await fireEvent.click(screen.getByRole("button", { name: "Show filter and selection rows" }));

    expect(titles()).toEqual(originalTitles);
    expect(screen.getByText("Page 2 / 2")).toBeInTheDocument();
    expect(onRequestChange).toHaveBeenCalledTimes(originalRequests);
    expect(readGridPrefs("toolbar-expansion-test")?.barsCollapsed ?? false).toBe(false);
  });

  it("does not add a filter reset row for sort, artwork or selection changes", async () => {
    const { container } = render(EntityGrid, {
      cards: Array.from({ length: 6 }, (_, index) => card(index)),
      prefsKey: undefined,
    });
    await fireEvent.click(screen.getByRole("button", { name: "Sort ascending; switch to descending" }));
    expect(container.querySelector(".filter-row")).toBeNull();
    const sort = screen.getByRole("button", { name: "Sort by" });
    sort.focus();
    await fireEvent.keyDown(sort, { key: "ArrowDown" });
    await fireEvent.pointerUp(await screen.findByRole("option", { name: "Rating" }));
    expect(container.querySelector(".filter-row")).toBeNull();
    await fireEvent.click(screen.getByRole("button", { name: "Media wall" }));
    expect(container.querySelector(".filter-row")).toBeNull();
    await fireEvent.click(screen.getByRole("button", { name: "Select" }));
    await fireEvent.click(screen.getByRole("button", { name: "Select all" }));
    expect(container.querySelector(".filter-row")).toBeNull();
  });

  it("clears search without resetting sort, artwork layout or selection", async () => {
    const onSelectionChange = vi.fn();
    render(EntityGrid, {
      cards: Array.from({ length: 6 }, (_, index) => card(index)),
      initialMediaWall: true,
      onSelectionChange,
      prefsKey: undefined,
    });
    await fireEvent.click(screen.getByRole("button", { name: "Sort ascending; switch to descending" }));
    await fireEvent.input(screen.getByRole("searchbox"), { target: { value: "Video" } });
    await fireEvent.click(screen.getByRole("button", { name: "Select" }));
    await fireEvent.click(screen.getByRole("button", { name: "Select all" }));
    const selectedIds = onSelectionChange.mock.lastCall?.[0];
    expect(selectedIds).toHaveLength(6);
    await fireEvent.click(screen.getByRole("button", { name: "Clear search and filters" }));

    expect(screen.getByRole("searchbox")).toHaveValue("");
    expect(screen.getByRole("button", { name: "Sort descending; switch to ascending" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Media wall" })).toHaveAttribute("aria-pressed", "true");
    expect(onSelectionChange.mock.lastCall?.[0]).toEqual(selectedIds);
    await waitFor(() => expect(screen.queryByRole("button", { name: "Clear search and filters" })).not.toBeInTheDocument());
  });

  it("can render grid cards as a metadata-free media wall", async () => {
    const cards = Array.from({ length: 6 }, (_, index) => card(index));
    const { container } = render(EntityGrid, {
      props: {
        cards,
        prefsKey: undefined,
      },
    });

    expect(container.querySelector(".thumbnail-caption")).not.toBeNull();

    await fireEvent.click(screen.getByRole("button", { name: "Media wall" }));

    await waitFor(() => {
      expect(container.querySelector(".cards")?.classList.contains("is-media-wall")).toBe(true);
      expect(container.querySelector(".thumbnail-caption")).toBeNull();
    });
  });

  it("uses the media wall default when no saved preference exists", async () => {
    const cards = Array.from({ length: 6 }, (_, index) => card(index));
    const { container } = render(EntityGrid, {
      props: {
        cards,
        initialMediaWall: true,
        prefsKey: "media-wall-default-test",
      },
    });

    await waitFor(() => {
      expect(container.querySelector(".cards")?.classList.contains("is-media-wall")).toBe(true);
      expect(container.querySelector(".thumbnail-caption")).toBeNull();
    });
  });

  it("persists the media wall preference by grid key", async () => {
    const cards = Array.from({ length: 6 }, (_, index) => card(index));
    const { container, unmount } = render(EntityGrid, {
      props: {
        cards,
        prefsKey: "media-wall-persist-test",
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Media wall" }));

    await waitFor(() => {
      expect(readGridPrefs("media-wall-persist-test")?.mediaWall).toBe(true);
      expect(container.querySelector(".cards")?.classList.contains("is-media-wall")).toBe(true);
    });

    unmount();

    const next = render(EntityGrid, {
      props: {
        cards,
        prefsKey: "media-wall-persist-test",
      },
    });

    await waitFor(() => {
      expect(next.container.querySelector(".cards")?.classList.contains("is-media-wall")).toBe(true);
    });
  });

  it("lets a saved media wall preference override the default", async () => {
    seedGridPrefs("media-wall-override-test", { mediaWall: false });
    const cards = Array.from({ length: 6 }, (_, index) => card(index));
    const { container } = render(EntityGrid, {
      props: {
        cards,
        initialMediaWall: true,
        prefsKey: "media-wall-override-test",
      },
    });

    await waitFor(() => {
      expect(container.querySelector(".cards")?.classList.contains("is-media-wall")).toBe(false);
      expect(container.querySelector(".thumbnail-caption")).not.toBeNull();
    });
  });

  it("can render embedded grids without docked controls or pagination chrome", () => {
    const cards = Array.from({ length: 6 }, (_, index) => card(index));
    const { container } = render(EntityGrid, {
      props: {
        cards,
        dockControls: false,
        showPagination: false,
        prefsKey: undefined,
      },
    });

    expect(container.querySelector(".entity-grid")?.classList.contains("is-static")).toBe(true);
    expect(container.querySelector(".pagination-shell")).toBeNull();
    expect(screen.queryByText("Page 1 / 1")).not.toBeInTheDocument();
  });
});

function card(index: number): EntityThumbnailCard {
  return {
    entity: {
      id: `video-${index}`,
      kind: "video",
      title: `Video ${index.toString().padStart(4, "0")}`,
      parentEntityId: null,
      sortOrder: null,
      relationships: [],
      capabilities: [],
      childrenByKind: [],
    },
    aspectRatio: "video",
    cover: null,
    hover: {
      kind: "none",
    },
  };
}

function createLocalStorageStub(): Storage {
  const store = new Map<string, string>();
  return {
    get length() {
      return store.size;
    },
    clear: () => store.clear(),
    getItem: (key: string) => store.get(key) ?? null,
    key: (index: number) => [...store.keys()][index] ?? null,
    removeItem: (key: string) => {
      store.delete(key);
    },
    setItem: (key: string, value: string) => {
      store.set(key, String(value));
    },
  };
}

function createMatchMedia(matches: boolean): typeof window.matchMedia {
  return vi.fn().mockImplementation((query: string): MediaQueryList => ({
    matches,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  }));
}
