import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { createRawSnippet } from "svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { personCard } from "../thumbnails/entity-thumbnail-test-fixtures";
import EntityShelf from "./EntityShelf.svelte";

const item = createRawSnippet(() => ({ render: () => "<span>Entity</span>" }));
const cards = ["Actor", "Director"].map((subtitle) => ({
  ...personCard(), subtitle,
}));

describe("EntityShelf navigation", () => {
  it("can shrink inside a grid without its thumbnails widening the page", () => {
    render(EntityShelf, { label: "Preview", cards, item });
    expect(screen.getByRole("region", { name: "Preview" })).toHaveClass("min-w-0");
  });
  let resize: () => void;
  beforeEach(() => {
    vi.stubGlobal("ResizeObserver", class {
      constructor(callback: () => void) { resize = callback; }
      observe() {}
      disconnect() {}
    });
    vi.stubGlobal("matchMedia", vi.fn(() => ({ matches: false })));
  });
  afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

  it("shows controls only for overflow and disables them at each boundary", async () => {
    const { container } = render(EntityShelf, { label: "Cast", cards, item, compact: true });
    expect(screen.queryByRole("button", { name: "Next Cast" })).not.toBeInTheDocument();
    const scroller = container.querySelector(".shelf-items") as HTMLDivElement;
    Object.defineProperties(scroller, {
      clientWidth: { value: 400, configurable: true },
      scrollWidth: { value: 1000, configurable: true },
    });
    scroller.scrollBy = vi.fn();
    resize();
    await waitFor(() => expect(screen.getByRole("button", { name: "Previous Cast" })).toBeDisabled());
    await fireEvent.click(screen.getByRole("button", { name: "Next Cast" }));
    expect(scroller.scrollBy).toHaveBeenCalledWith({ left: 340, behavior: "smooth" });
    scroller.scrollLeft = 600;
    await fireEvent.scroll(scroller);
    expect(screen.getByRole("button", { name: "Next Cast" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Previous Cast" })).toBeEnabled();
    vi.mocked(window.matchMedia).mockReturnValue({ matches: true } as MediaQueryList);
    await fireEvent.click(screen.getByRole("button", { name: "Previous Cast" }));
    expect(scroller.scrollBy).toHaveBeenLastCalledWith({ left: -340, behavior: "instant" });
    Object.defineProperty(scroller, "clientWidth", { value: 1200 });
    scroller.scrollLeft = 0;
    resize();
    await waitFor(() => expect(screen.queryByRole("button", { name: "Next Cast" })).not.toBeInTheDocument());
  });

  it("rechecks overflow when loaded items change and supports multiple credits for one entity", async () => {
    const { container, rerender } = render(EntityShelf, { label: "Cast", cards: [], item, compact: true });
    const scroller = container.querySelector(".shelf-items") as HTMLDivElement;
    Object.defineProperties(scroller, { clientWidth: { value: 200 }, scrollWidth: { get: () => scroller.children.length * 150 } });
    await rerender({ cards });
    await waitFor(() => expect(screen.getByRole("button", { name: "Next Cast" })).toBeEnabled());
    expect(screen.getAllByText("Entity")).toHaveLength(2);
    await rerender({ cards: [] });
    await waitFor(() => expect(screen.queryByRole("button", { name: "Next Cast" })).not.toBeInTheDocument());
  });
});
