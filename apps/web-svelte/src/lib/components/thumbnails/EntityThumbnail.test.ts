import { fireEvent, render, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { tick } from "svelte";
import EntityThumbnail from "./EntityThumbnail.svelte";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

// jsdom lacks TouchEvent, so synthesize a cancelable event carrying a touches list.
function touchEvent(type: string, touches: Array<{ clientX: number; clientY: number }>): Event {
  const event = new Event(type, { bubbles: true, cancelable: true });
  Object.defineProperty(event, "touches", { value: touches, configurable: true });
  return event;
}

const loadTrickplayFrames = vi.fn();

vi.mock("@prismedia/ui-svelte", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@prismedia/ui-svelte")>();
  return {
    ...actual,
    loadTrickplayFrames: (...args: unknown[]) => loadTrickplayFrames(...args),
  };
});

describe("EntityThumbnail", () => {
  beforeEach(() => {
    loadTrickplayFrames.mockClear();
    loadTrickplayFrames.mockResolvedValue([
      { start: 0, end: 10, x: 0, y: 0, width: 160, height: 90, url: "/Videos/1/Trickplay/280/0.jpg" },
      { start: 10, end: 20, x: 160, y: 0, width: 160, height: 90, url: "/Videos/1/Trickplay/280/0.jpg" },
    ]);
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
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("shows the first sprite frame after hover intent settles", async () => {
    vi.useFakeTimers();
    const { container } = render(EntityThumbnail, {
      props: {
        card: spriteCard(),
      },
    });
    const media = container.querySelector(".media") as HTMLElement;
    Object.defineProperty(media, "getBoundingClientRect", {
      configurable: true,
      value: () => ({ left: 0, width: 100 }),
    });

    await fireEvent(media, pointerEvent("pointerenter", 50));

    expect(loadTrickplayFrames).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(140);

    await waitFor(() => {
      expect(loadTrickplayFrames).toHaveBeenCalledWith("/Videos/1/Trickplay/280/tiles.m3u8");
      expect(container.querySelector(".sprite-overlay")).not.toBeNull();
    });
  });

  it("does not scrub or capture the pointer on touch so rows stay scrollable", async () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: spriteCard(),
      },
    });
    const media = container.querySelector(".media") as HTMLElement;
    media.setPointerCapture = vi.fn();
    Object.defineProperty(media, "getBoundingClientRect", {
      configurable: true,
      value: () => ({ left: 0, width: 100 }),
    });

    // A touch drag must not capture the pointer — the browser keeps owning the gesture so the
    // surrounding horizontal row can scroll. (Scrubbing is a desktop pointer-hover interaction.)
    await fireEvent(media, pointerEvent("pointerdown", 10, { pointerType: "touch", pointerId: 7 }));
    await fireEvent(media, pointerEvent("pointermove", 90, { pointerType: "touch", pointerId: 7 }));

    expect(media.setPointerCapture).not.toHaveBeenCalled();
  });

  it("scrubs sprite trickplay from a horizontal touch drag", async () => {
    vi.useFakeTimers();
    const { container } = render(EntityThumbnail, {
      props: {
        card: spriteCard(),
      },
    });
    await tick();
    const media = container.querySelector(".media") as HTMLElement;
    Object.defineProperty(media, "getBoundingClientRect", {
      configurable: true,
      value: () => ({ left: 0, width: 100 }),
    });

    media.dispatchEvent(touchEvent("touchstart", [{ clientX: 10, clientY: 10 }]));
    // A horizontal drag scrubs immediately and preventDefaults to stop the row scrolling.
    const move = touchEvent("touchmove", [{ clientX: 90, clientY: 12 }]);
    media.dispatchEvent(move);
    await vi.advanceTimersByTimeAsync(0);

    expect(move.defaultPrevented).toBe(true);
    const overlay = container.querySelector<HTMLElement>(".sprite-overlay");
    expect(loadTrickplayFrames).toHaveBeenCalledWith("/Videos/1/Trickplay/280/tiles.m3u8");
    expect(overlay?.style.backgroundPosition).toContain("100%");
  });

  it("lets a vertical touch swipe scroll without scrubbing", async () => {
    vi.useFakeTimers();
    const { container } = render(EntityThumbnail, {
      props: {
        card: spriteCard(),
      },
    });
    await tick();
    const media = container.querySelector(".media") as HTMLElement;
    Object.defineProperty(media, "getBoundingClientRect", {
      configurable: true,
      value: () => ({ left: 0, width: 100 }),
    });

    media.dispatchEvent(touchEvent("touchstart", [{ clientX: 10, clientY: 10 }]));
    // A vertical drag is a scroll: it resolves to "scroll" and never preventDefaults.
    media.dispatchEvent(touchEvent("touchmove", [{ clientX: 12, clientY: 60 }]));
    const move = touchEvent("touchmove", [{ clientX: 12, clientY: 90 }]);
    media.dispatchEvent(move);

    expect(move.defaultPrevented).toBe(false);
  });

  it("keeps touch taps navigable when a thumbnail can scrub", async () => {
    const onActivate = vi.fn();
    const { container } = render(EntityThumbnail, {
      props: {
        card: spriteCard(),
        linkable: false,
        onActivate,
      },
    });
    const surface = container.querySelector<HTMLElement>(".entity-thumbnail");
    const media = container.querySelector(".media") as HTMLElement;
    media.setPointerCapture = vi.fn();
    media.releasePointerCapture = vi.fn();
    Object.defineProperty(media, "getBoundingClientRect", {
      configurable: true,
      value: () => ({ left: 0, width: 100 }),
    });

    await fireEvent(media, pointerEvent("pointerdown", 50, { pointerType: "touch", pointerId: 7 }));
    await fireEvent(media, pointerEvent("pointermove", 54, { pointerType: "touch", pointerId: 7 }));
    expect(media.setPointerCapture).not.toHaveBeenCalled();
    await fireEvent(media, pointerEvent("pointerup", 54, { pointerType: "touch", pointerId: 7 }));
    await fireEvent.click(surface!);

    expect(onActivate).toHaveBeenCalledWith(spriteCard());
  });

  it("does not start mouse scrubbing from linkable hover-preview thumbnails", async () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: imageSequenceCard(),
      },
    });
    const media = container.querySelector(".media") as HTMLElement;
    media.setPointerCapture = vi.fn();
    Object.defineProperty(media, "getBoundingClientRect", {
      configurable: true,
      value: () => ({ left: 0, width: 100 }),
    });

    await fireEvent(media, pointerEvent("pointerdown", 90));
    await fireEvent.focus(container.querySelector<HTMLElement>(".entity-thumbnail")!);

    expect(media.setPointerCapture).not.toHaveBeenCalled();
    expect(container.querySelector<HTMLImageElement>(".media > img")?.getAttribute("src")).toBe("/assets/pages/1.jpg");
  });

  it("resolves default entity links inside the shared thumbnail", () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: personCard(),
      },
    });

    const link = container.querySelector<HTMLAnchorElement>("a.entity-thumbnail");
    expect(link?.getAttribute("href")).toBe("/people/person-1");
  });

  it("can open shared thumbnail links in a safe new tab", () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: personCard(),
        linkTarget: "_blank",
      },
    });

    const link = container.querySelector<HTMLAnchorElement>("a.entity-thumbnail");
    expect(link?.getAttribute("target")).toBe("_blank");
    expect(link?.getAttribute("rel")).toBe("noopener noreferrer");
  });

  it("renders remote cover artwork without forwarding the app referrer", () => {
    const coverUrl =
      "https://uploads.mangadex.org/covers/2d3114e5-43fb-4e10-9129-ebc2014489f8/6187c6cf-8f55-4b2e-874a-3e689b000623.jpg.512.jpg";
    const card = {
      ...personCard(),
      cover: {
        alt: "MangaDex cover",
        src: coverUrl,
      },
    };

    const { container } = render(EntityThumbnail, {
      props: { card },
    });

    const image = container.querySelector<HTMLImageElement>(".media > img");
    expect(image).toHaveAttribute("src", coverUrl);
    expect(image).toHaveAttribute("referrerpolicy", "no-referrer");
  });

  it("resolves nested gallery cards to their gallery detail route", () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: galleryCard(),
        selectable: false,
      },
    });

    const link = container.querySelector<HTMLAnchorElement>("a.entity-thumbnail");
    expect(link?.getAttribute("href")).toBe("/galleries/gallery-2");
  });

  it("can disable the default link and toggle selection from the card surface", async () => {
    const onSelectedChange = vi.fn();
    const { container } = render(EntityThumbnail, {
      props: {
        card: personCard(),
        linkable: false,
        selectable: true,
        selected: true,
        onSelectedChange,
      },
    });

    const surface = container.querySelector<HTMLElement>(".entity-thumbnail");
    expect(container.querySelector("a.entity-thumbnail")).toBeNull();
    expect(surface?.getAttribute("role")).toBe("checkbox");
    expect(surface?.getAttribute("aria-checked")).toBe("true");

    await fireEvent.click(surface!);

    expect(onSelectedChange).toHaveBeenCalledWith(false);
  });

  it("can activate from the card surface while keeping checkbox selection separate", async () => {
    const onActivate = vi.fn();
    const onSelectedChange = vi.fn();
    const { container } = render(EntityThumbnail, {
      props: {
        card: personCard(),
        linkable: false,
        selectable: true,
        selected: true,
        onActivate,
        onSelectedChange,
      },
    });

    const surface = container.querySelector<HTMLElement>(".entity-thumbnail");
    const checkbox = container.querySelector<HTMLInputElement>(".selection");

    await fireEvent.click(surface!);
    expect(onActivate).toHaveBeenCalledWith(personCard());
    expect(onSelectedChange).not.toHaveBeenCalled();

    await fireEvent.click(checkbox!);
    await fireEvent.change(checkbox!, { target: { checked: false } });
    expect(onSelectedChange).toHaveBeenCalledWith(false);
  });

  it("can render as a static visual inside a parent control", () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: personCard(),
        interactive: false,
        linkable: false,
      },
    });

    const surface = container.querySelector<HTMLElement>(".entity-thumbnail");
    expect(container.querySelector("a.entity-thumbnail")).toBeNull();
    expect(surface?.getAttribute("role")).toBeNull();
    expect(surface?.getAttribute("tabindex")).toBeNull();
    expect(surface?.classList.contains("is-static")).toBe(true);
  });

  it("renders credit subtitles when present", () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: {
          ...personCard(),
          subtitle: "Character Ronnie",
        },
      },
    });

    expect(container.textContent).toContain("Character Ronnie");
  });

  it("shows a skeleton while async cover images are loading", async () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: {
          ...personCard(),
          cover: {
            alt: "Tim Robinson",
            src: "/assets/people/tim.jpg",
          },
        },
      },
    });

    const media = container.querySelector<HTMLElement>(".media");
    const image = container.querySelector<HTMLImageElement>(".media > img");

    expect(image?.getAttribute("loading")).toBe("lazy");
    expect(image?.getAttribute("decoding")).toBe("async");
    expect(image?.getAttribute("fetchpriority")).toBe("low");
    expect(media?.classList.contains("is-image-loading")).toBe(true);
    expect(container.querySelector(".image-loading-skeleton")).not.toBeNull();

    await fireEvent.load(image!);

    await waitFor(() => {
      expect(media?.classList.contains("is-image-loading")).toBe(false);
      expect(container.querySelector(".image-loading-skeleton")).toBeNull();
    });
  });

  it("allows scrolling grids to opt into eager cover image loading", () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: {
          ...personCard(),
          cover: {
            alt: "Tim Robinson",
            src: "/assets/people/tim.jpg",
          },
        },
        imageFetchPriority: "auto",
        imageLoading: "eager",
      },
    });

    const image = container.querySelector<HTMLImageElement>(".media > img");

    expect(image?.getAttribute("loading")).toBe("eager");
    expect(image?.getAttribute("fetchpriority")).toBe("auto");
  });

  it("renders image-sequence thumbnails as the first still until hovered", async () => {
    vi.useFakeTimers();
    const { container } = render(EntityThumbnail, {
      props: {
        card: imageSequenceCard(),
      },
    });
    const media = container.querySelector(".media") as HTMLElement;
    Object.defineProperty(media, "getBoundingClientRect", {
      configurable: true,
      value: () => ({ left: 0, width: 100 }),
    });

    expect(container.querySelector<HTMLImageElement>(".media > img")?.getAttribute("src")).toBe("/assets/pages/1.jpg");

    await fireEvent(media, pointerEvent("pointerenter", 90));
    expect(container.querySelector<HTMLImageElement>(".media > img")?.getAttribute("src")).toBe("/assets/pages/1.jpg");
    await vi.advanceTimersByTimeAsync(140);

    const activeImage = container.querySelector<HTMLImageElement>(".media > img");
    expect(activeImage?.getAttribute("src")).toBe("/assets/pages/3.jpg");
    expect(container.querySelector(".sequence-rail span.is-active")).not.toBeNull();
  });

  it("scrubs image-sequence thumbnails from a mouse hover drag", async () => {
    vi.useFakeTimers();
    const { container } = render(EntityThumbnail, {
      props: {
        card: imageSequenceCard(),
      },
    });
    const media = container.querySelector(".media") as HTMLElement;
    Object.defineProperty(media, "getBoundingClientRect", {
      configurable: true,
      value: () => ({ left: 0, width: 100 }),
    });

    await fireEvent(media, pointerEvent("pointerenter", 0));
    await vi.advanceTimersByTimeAsync(200);
    await fireEvent(media, pointerEvent("pointermove", 90));
    await vi.advanceTimersByTimeAsync(0);

    expect(container.querySelector<HTMLImageElement>(".media > img")?.getAttribute("src")).toBe("/assets/pages/3.jpg");
    expect(container.querySelector(".sequence-rail span.is-active")).not.toBeNull();
  });

  it("keeps image-sequence hover previews on the selected frame until the user scrubs", async () => {
    vi.useFakeTimers();
    const { container } = render(EntityThumbnail, {
      props: {
        card: imageSequenceCard(),
      },
    });
    const media = container.querySelector(".media") as HTMLElement;
    Object.defineProperty(media, "getBoundingClientRect", {
      configurable: true,
      value: () => ({ left: 0, width: 100 }),
    });

    await fireEvent(media, pointerEvent("pointerenter", 0));
    expect(container.querySelector<HTMLImageElement>(".media > img")?.getAttribute("src")).toBe("/assets/pages/1.jpg");

    await vi.advanceTimersByTimeAsync(1_500);
    expect(container.querySelector<HTMLImageElement>(".media > img")?.getAttribute("src")).toBe("/assets/pages/1.jpg");

    await fireEvent(media, pointerEvent("pointermove", 90));

    expect(container.querySelector<HTMLImageElement>(".media > img")?.getAttribute("src")).toBe("/assets/pages/3.jpg");
  });

  it("does not load hover previews while previews are disabled", async () => {
    vi.useFakeTimers();
    const { container } = render(EntityThumbnail, {
      props: {
        card: spriteCard(),
        hoverPreviewsEnabled: false,
      },
    });
    const media = container.querySelector(".media") as HTMLElement;
    Object.defineProperty(media, "getBoundingClientRect", {
      configurable: true,
      value: () => ({ left: 0, width: 100 }),
    });

    await fireEvent(media, pointerEvent("pointerenter", 50));
    await vi.advanceTimersByTimeAsync(500);

    expect(loadTrickplayFrames).not.toHaveBeenCalled();
    expect(container.querySelector(".sprite-overlay")).toBeNull();
  });

  it("does not load hover previews while the caller temporarily suppresses them", async () => {
    vi.useFakeTimers();
    const { container } = render(EntityThumbnail, {
      props: {
        card: spriteCard(),
        hoverPreviewSuppressed: () => true,
      },
    });
    const media = container.querySelector(".media") as HTMLElement;
    Object.defineProperty(media, "getBoundingClientRect", {
      configurable: true,
      value: () => ({ left: 0, width: 100 }),
    });

    await fireEvent(media, pointerEvent("pointerenter", 50));
    await vi.advanceTimersByTimeAsync(500);

    expect(loadTrickplayFrames).not.toHaveBeenCalled();
    expect(container.querySelector(".sprite-overlay")).toBeNull();
  });

  it("renders card titles as wrapping static text", () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: personCard(),
      },
    });

    expect(container.querySelector("h3")?.textContent).toContain("Tim Robinson");
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

    expect(container.querySelector(".glass-info")).not.toBeNull();
    expect(container.querySelector(".chips")?.textContent).toContain("Page 12");
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

function spriteCard(): EntityThumbnailCard {
  return {
    entity: {
      id: "video-1",
      kind: "video",
      title: "Video",
      parentEntityId: null,
      sortOrder: null,
      relationships: [],
      capabilities: [],
      childrenByKind: [],
    },
    aspectRatio: "video",
    cover: {
      alt: "Video cover",
      src: "/assets/videos/1/thumb.jpg",
    },
    hover: {
      kind: "sprite",
      vttUrl: "/Videos/1/Trickplay/280/tiles.m3u8",
    },
  };
}

function imageSequenceCard(): EntityThumbnailCard {
  return {
    entity: {
      id: "book-1",
      kind: "book",
      title: "Book",
      parentEntityId: null,
      sortOrder: null,
      relationships: [],
      capabilities: [],
      childrenByKind: [],
    },
    aspectRatio: "poster",
    cover: null,
    hover: {
      kind: "image-sequence",
      assets: [
        { alt: "Page 1", src: "/assets/pages/1.jpg" },
        { alt: "Page 2", src: "/assets/pages/2.jpg" },
        { alt: "Page 3", src: "/assets/pages/3.jpg" },
      ],
    },
  };
}

function personCard(): EntityThumbnailCard {
  return {
    entity: {
      id: "person-1",
      kind: "person",
      title: "Tim Robinson",
      parentEntityId: null,
      sortOrder: null,
      relationships: [],
      capabilities: [],
      childrenByKind: [],
    },
    aspectRatio: "portrait",
    cover: null,
    hover: {
      kind: "none",
    },
  };
}

function galleryCard(): EntityThumbnailCard {
  return {
    entity: {
      id: "gallery-2",
      kind: "gallery",
      title: "A secondGallery",
      parentEntityId: "gallery-1",
      sortOrder: 0,
      relationships: [],
      capabilities: [],
      childrenByKind: [],
    },
    aspectRatio: "square",
    cover: {
      alt: "A secondGallery cover",
      src: "/assets/galleries/2/thumb.jpg",
    },
    hover: {
      kind: "none",
    },
  };
}

function episodeCard(): EntityThumbnailCard {
  return {
    ...spriteCard(),
    custom: {
      bottomLeft: {
        label: "S1 E2",
        title: "Season 1, Episode 2",
      },
    },
    entity: {
      ...spriteCard().entity,
      capabilities: [
        {
          kind: "flags",
          isFavorite: false,
          isNsfw: true,
          isOrganized: true,
        },
        {
          kind: "rating",
          value: 4,
        },
      ],
    },
    meta: [{ icon: "video", label: "1080p" }],
  };
}

function bookPageCard(): EntityThumbnailCard {
  return {
    entity: {
      id: "page-12",
      kind: "book-page",
      title: "Page 12",
      parentEntityId: "chapter-1",
      sortOrder: 12,
      relationships: [],
      capabilities: [],
      childrenByKind: [],
    },
    aspectRatio: "poster",
    cover: {
      alt: "Page 12",
      src: "/assets/pages/page-12.jpg",
    },
    hover: {
      kind: "none",
    },
    meta: [{ icon: "book", label: "Page 12" }],
  };
}

function pointerEvent(
  type: string,
  clientX: number,
  options: { clientY?: number; pointerId?: number; pointerType?: string } = {},
) {
  const event = new Event(type, { bubbles: true, cancelable: true });
  Object.defineProperty(event, "clientX", { value: clientX });
  Object.defineProperty(event, "clientY", { value: options.clientY ?? 0 });
  Object.defineProperty(event, "pointerId", { value: options.pointerId ?? 1 });
  Object.defineProperty(event, "pointerType", { value: options.pointerType ?? "mouse" });
  return event;
}
