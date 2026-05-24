import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
import EntityGrid from "./EntityGrid.test-harness.svelte";

describe("EntityGrid pagination", () => {
  beforeEach(() => {
    vi.stubGlobal("requestAnimationFrame", vi.fn((callback: FrameRequestCallback) => {
      callback(0);
      return 1;
    }));
    vi.stubGlobal("cancelAnimationFrame", vi.fn());
    vi.stubGlobal("ResizeObserver", class {
      observe = vi.fn();
      disconnect = vi.fn();
    });
    Object.defineProperty(HTMLElement.prototype, "scrollTo", {
      configurable: true,
      value: vi.fn(),
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
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
      expect(renderedCount).toBe(250);
      expect(container.querySelectorAll(".entity-thumbnail").length).toBe(250);
      expect(screen.getByText("Page 1 / 18")).toBeInTheDocument();
    });
  });

  it("lets the docked pagination bar choose page size and move pages", async () => {
    const cards = Array.from({ length: 600 }, (_, index) => card(index));
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
      expect(screen.getByText("Page 1 / 6")).toBeInTheDocument();
    });

    await fireEvent.click(screen.getByLabelText("Next page"));

    await waitFor(() => {
      expect(screen.getByText("101–200")).toBeInTheDocument();
      expect(screen.getByText("Page 2 / 6")).toBeInTheDocument();
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
      expect(container.querySelectorAll(".entity-thumbnail")).toHaveLength(150);
      expect(container.querySelector(".pagination-shell")).not.toBeNull();
      expect(screen.getByLabelText("Per page")).toHaveTextContent("250");
      expect(screen.getByText("Page 1 / 1")).toBeInTheDocument();
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
      expect.arrayContaining([cards[0], cards[249]]),
    );
    expect(onCardActivate.mock.calls[0][1]).toHaveLength(250);
  });

  it("can render grid cards as a metadata-free media wall", async () => {
    const cards = Array.from({ length: 6 }, (_, index) => card(index));
    const { container } = render(EntityGrid, {
      props: {
        cards,
        prefsKey: undefined,
      },
    });

    expect(container.querySelector(".glass-info")).not.toBeNull();

    await fireEvent.click(screen.getByRole("button", { name: "Media wall" }));

    await waitFor(() => {
      expect(container.querySelector(".cards")?.classList.contains("is-media-wall")).toBe(true);
      expect(container.querySelector(".glass-info")).toBeNull();
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
