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
    loadTrickplayFrames.mockResolvedValue([
      { start: 0, end: 10, x: 0, y: 0, width: 160, height: 90, url: "/Videos/1/Trickplay/280/0.jpg" },
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

  it("shows the first sprite frame as soon as a trickplay thumbnail is hovered", async () => {
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

    await waitFor(() => {
      expect(loadTrickplayFrames).toHaveBeenCalledWith("/Videos/1/Trickplay/280/tiles.m3u8");
      expect(container.querySelector(".sprite-overlay")).not.toBeNull();
    });
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

    await fireEvent(media, pointerEvent("pointermove", 90));

    const activeImage = container.querySelector<HTMLImageElement>(".media > img");
    expect(activeImage?.getAttribute("src")).toBe("/assets/pages/3.jpg");
    expect(container.querySelector(".sequence-rail span.is-active")).not.toBeNull();
  });

  it("cycles image-sequence thumbnails while hovered", async () => {
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

    await vi.advanceTimersByTimeAsync(700);

    await waitFor(() => {
      expect(container.querySelector<HTMLImageElement>(".media > img")?.getAttribute("src")).toBe("/assets/pages/2.jpg");
    });
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

function pointerEvent(type: string, clientX: number) {
  const event = new Event(type, { bubbles: true, cancelable: true });
  Object.defineProperty(event, "clientX", { value: clientX });
  return event;
}
