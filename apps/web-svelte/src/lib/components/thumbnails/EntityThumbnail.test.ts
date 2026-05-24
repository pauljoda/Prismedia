import { fireEvent, render, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import EntityThumbnail from "./EntityThumbnail.svelte";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

const loadTrickplayFrames = vi.fn();

vi.mock("@prismedia/ui-svelte", () => ({
  loadTrickplayFrames: (...args: unknown[]) => loadTrickplayFrames(...args),
}));

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

  it("scrubs sprite trickplay from touch pointer drag", async () => {
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

    await fireEvent(media, pointerEvent("pointerdown", 10, { pointerType: "touch", pointerId: 7 }));
    await fireEvent(media, pointerEvent("pointermove", 90, { pointerType: "touch", pointerId: 7 }));

    await waitFor(() => {
      const overlay = container.querySelector<HTMLElement>(".sprite-overlay");
      expect(loadTrickplayFrames).toHaveBeenCalledWith("/Videos/1/Trickplay/280/tiles.m3u8");
      expect(overlay?.style.backgroundPosition).toContain("100%");
    });
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

  it("resolves default entity links inside the shared thumbnail", () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: personCard(),
      },
    });

    const link = container.querySelector<HTMLAnchorElement>("a.entity-thumbnail");
    expect(link?.getAttribute("href")).toBe("/people/person-1");
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

  it("scrubs image-sequence thumbnails from touch pointer drag", async () => {
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

    await fireEvent(media, pointerEvent("pointerdown", 10, { pointerType: "touch", pointerId: 8 }));
    await fireEvent(media, pointerEvent("pointermove", 90, { pointerType: "touch", pointerId: 8 }));

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

  it("allows callers to choose thumbnail title alignment", () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: personCard(),
        titleAlign: "center",
      },
    });

    expect(container.querySelector("h3")?.classList.contains("title-align-center")).toBe(true);
    expect(container.querySelector("h3")?.classList.contains("title-size-default")).toBe(true);
  });

  it("allows callers to choose compact thumbnail title sizing", () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: personCard(),
        titleSize: "compact",
      },
    });

    expect(container.querySelector("h3")?.classList.contains("title-size-compact")).toBe(true);
  });

  it("aligns fallback subtitles with the thumbnail title", () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: {
          ...personCard(),
          subtitle: "Character Ronnie",
        },
        titleAlign: "center",
      },
    });

    expect(container.querySelector(".subtitle")?.classList.contains("title-align-center")).toBe(true);
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
