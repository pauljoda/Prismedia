import { fireEvent, render } from "@testing-library/svelte";
import { tick } from "svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { THUMBNAIL_META_ICON } from "$lib/api/generated/codes";
import EntityThumbnail from "./EntityThumbnail.svelte";
import {
  bookPageCard,
  episodeCard,
  personCard,
} from "./entity-thumbnail-test-fixtures";

describe("EntityThumbnail presentation", () => {
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
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("renders a native-style caption below the artwork surface", () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: personCard(),
      },
    });

    expect(container.querySelector("h3")?.textContent).toContain("Tim Robinson");
    expect(container.querySelector(".media")?.nextElementSibling).toHaveClass("thumbnail-caption");
    expect(container.querySelector(".ticker-shell")).toBeNull();
    expect(container.querySelector(".ticker-title")).toBeNull();
  });

  it("elevates NSFW, rating, and position badges into the thumbnail media", () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: episodeCard(),
      },
    });

    expect(container.querySelector(".top-badges .danger")?.getAttribute("aria-label")).toBe("NSFW");
    expect(container.querySelector(".bottom-left-badges .position-badge")?.textContent?.trim()).toBe("S1 E2");
    const ratingBadge = container.querySelector(".bottom-right-badges .rating-badge");
    expect(ratingBadge?.textContent?.trim()).toBe("4");
    expect(ratingBadge?.querySelectorAll("svg")).toHaveLength(1);
    expect(container.querySelector(".chips")?.textContent).toContain("1080p");
    expect(container.querySelector(".chips")?.textContent).not.toContain("S1 E2");
    expect(container.querySelector(".chips")?.textContent).not.toContain("4");
  });

  it("shows book page metadata when media-only mode is not requested", () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: bookPageCard(),
      },
    });

    expect(container.querySelector(".thumbnail-caption")).not.toBeNull();
    expect(container.querySelector(".chips")?.textContent).toContain("Page 12");
  });

  it("exposes exact structural count units to assistive technology", () => {
    const card = episodeCard();
    card.meta = [
      { icon: THUMBNAIL_META_ICON.season, label: "2" },
      { icon: THUMBNAIL_META_ICON.episode, label: "18" },
    ];

    const { container } = render(EntityThumbnail, { props: { card } });
    const chips = [...container.querySelectorAll(".chip")];

    expect(chips.map((chip) => chip.getAttribute("aria-label"))).toEqual(["season 2", "episode 18"]);
  });

  it("replaces the entity-family fallback with an artwork-derived accent after the cover decodes", async () => {
    const pixels = new Uint8ClampedArray(12 * 12 * 4);
    for (let y = 0; y < 12; y += 1) {
      for (let x = 0; x < 12; x += 1) {
        const offset = (y * 12 + x) * 4;
        const color = x < 2 || y < 2 || x >= 10 || y >= 10
          ? [224, 216, 190]
          : x < 8
            ? [176, 28, 43]
            : [30, 82, 160];
        pixels.set([...color, 255], offset);
      }
    }
    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue({
      drawImage: vi.fn(),
      getImageData: vi.fn(() => ({ data: pixels })),
    } as unknown as CanvasRenderingContext2D);

    const { container } = render(EntityThumbnail, { props: { card: bookPageCard() } });
    const image = container.querySelector("img") as HTMLImageElement;
    Object.defineProperty(image, "naturalWidth", { configurable: true, value: 12 });
    Object.defineProperty(image, "naturalHeight", { configurable: true, value: 12 });
    await fireEvent.load(image);
    await tick();

    const thumbnail = container.querySelector(".entity-thumbnail") as HTMLElement;
    expect(thumbnail.style.getPropertyValue("--entity-accent")).not.toBe("#0ab3e6");
    expect(thumbnail.style.getPropertyValue("--entity-accent")).toMatch(/^#[0-9a-f]{6}$/);
  });
});
