import { fireEvent, render } from "@testing-library/svelte";
import { tick } from "svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ACQUISITION_STATUS, CAPABILITY_KIND, THUMBNAIL_META_ICON } from "$lib/api/generated/codes";
import EntityThumbnail from "./EntityThumbnail.svelte";
import {
  comicInstallmentCard,
  episodeCard,
  personCard,
  studioCard,
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

  it("keeps top-left metadata clear of the selection checkbox", () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: episodeCard(),
        selectable: true,
      },
    });

    expect(container.querySelector(".bottom-left-badges")).toHaveClass("has-selection");
  });

  it("puts readable status and metadata badges in list captions instead of covering the artwork", () => {
    const card = episodeCard();
    const flags = card.entity.capabilities.find((capability) => capability.kind === CAPABILITY_KIND.flags)!;
    Object.assign(flags, { isWanted: true });
    card.wantedStatus = ACQUISITION_STATUS.awaitingSelection;
    card.custom = { ...card.custom, sourceTag: { label: "Source" } };
    const { container } = render(EntityThumbnail, { props: { card, layout: "list" } });
    const caption = container.querySelector(".thumbnail-caption")!;
    expect(caption.querySelector(".wanted-badge")).toHaveTextContent("Choose release");
    expect(caption.querySelector(".position-badge")).toHaveTextContent("S1 E2");
    expect(caption.querySelector(".rating-badge")).toHaveTextContent("4");
    expect(caption.querySelector(".source-badge")).toHaveTextContent("Source");
    expect(caption.querySelector('[aria-label="NSFW"]')).toBeInTheDocument();
    expect(container.querySelector(".media .thumbnail-badges")).toBeNull();
    expect(caption.querySelectorAll('.thumbnail-badges [data-slot="badge"]')).toHaveLength(5);
  });

  it("retains badges on artwork when a list is explicitly media-only", () => {
    const { container } = render(EntityThumbnail, { props: { card: episodeCard(), layout: "list", mediaOnly: true } });
    expect(container.querySelector(".thumbnail-caption")).toBeNull();
    expect(container.querySelector('.media .position-badge[data-slot="badge"]')).toHaveTextContent("S1 E2");
  });

  it.each(["grid", "list"] as const)("respects host-owned status in %s layout", (layout) => {
    const card = episodeCard();
    const flags = card.entity.capabilities.find((capability) => capability.kind === CAPABILITY_KIND.flags)!;
    Object.assign(flags, { isWanted: true });
    const { container } = render(EntityThumbnail, { props: { card, layout, showWantedBadge: false } });
    expect(container.querySelector(".wanted-badge")).toBeNull();
    expect(container.querySelector('[aria-label="NSFW"]')).toBeInTheDocument();
  });

  it("shows cached comic installment page metadata when media-only mode is not requested", () => {
    const { container } = render(EntityThumbnail, {
      props: {
        card: comicInstallmentCard(),
      },
    });

    expect(container.querySelector(".thumbnail-caption")).not.toBeNull();
    expect(container.querySelector(".chips")?.textContent).toContain("24");
  });

  it("renders original Studio artwork on the definition-owned brand plate", () => {
    const { container } = render(EntityThumbnail, { props: { card: studioCard() } });

    expect(container.querySelector(".media")).toHaveClass("has-logo-art");
    expect(container.querySelector("img")).toHaveAttribute("src", "/assets/studios/hbo-logo.svg");
    expect(container.querySelector("img")).not.toHaveAttribute("srcset");
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

  it.each(["grid", "list"] as const)("keeps %s captions to two metadata chips on one row", (layout) => {
    const card = episodeCard();
    card.meta = [
      { icon: THUMBNAIL_META_ICON.duration, label: "03:52" },
      { icon: THUMBNAIL_META_ICON.video, label: "1080p" },
      { icon: THUMBNAIL_META_ICON.video, label: "H264" },
      { icon: THUMBNAIL_META_ICON.video, label: "MOV" },
    ];
    const { container } = render(EntityThumbnail, { props: { card, layout } });
    const chips = [...container.querySelectorAll(".chips .chip")];
    expect(container.querySelector(".chips")).toHaveClass("flex-nowrap");
    expect(chips).toHaveLength(2);
    expect(chips.map(chip => chip.textContent?.trim())).toEqual(["03:52", "1080p"]);
    for (const chip of chips) {
      expect(chip).toHaveClass("max-w-full", "min-w-0", "shrink");
      expect(chip.querySelector(".chip-label")).toHaveClass("truncate");
    }
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

    const { container } = render(EntityThumbnail, { props: { card: comicInstallmentCard() } });
    const image = container.querySelector("img") as HTMLImageElement;
    Object.defineProperty(image, "naturalWidth", { configurable: true, value: 12 });
    Object.defineProperty(image, "naturalHeight", { configurable: true, value: 12 });
    await fireEvent.load(image);
    await tick();

    const thumbnail = container.querySelector(".entity-thumbnail") as HTMLElement;
    expect(thumbnail.style.getPropertyValue("--entity-accent")).not.toBe("#0ab3e6");
    expect(thumbnail.style.getPropertyValue("--entity-accent")).toMatch(/^#[0-9a-f]{6}$/);
  });

  it("skips synchronous artwork analysis when a bulk grid opts out", async () => {
    const canvas = vi.spyOn(HTMLCanvasElement.prototype, "getContext");
    const { container } = render(EntityThumbnail, {
      props: { artworkReactive: false, card: comicInstallmentCard() },
    });
    const image = container.querySelector("img") as HTMLImageElement;
    Object.defineProperty(image, "naturalWidth", { configurable: true, value: 480 });
    Object.defineProperty(image, "naturalHeight", { configurable: true, value: 720 });

    await fireEvent.load(image);
    await tick();

    expect(canvas).not.toHaveBeenCalled();
  });
});
