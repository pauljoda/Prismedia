import { readFile } from "node:fs/promises";
import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { tick } from "svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import UniversalLightboxHarness from "./UniversalLightbox.test-harness.svelte";
import UniversalLightboxDetailsHarness from "./UniversalLightbox.details-test-harness.svelte";
import type { UniversalLightboxEntity } from "./universal-lightbox-media";

vi.mock("vidstack/player", () => ({}));
vi.mock("vidstack/player/layouts", () => ({}));
vi.mock("vidstack/player/ui", () => ({}));
vi.mock("vidstack", () => ({
  isHLSProvider: () => false,
}));

const still: UniversalLightboxEntity = {
  id: "image-1",
  kind: "image",
  title: "Still",
  capabilities: [
    { kind: "files", items: [{ role: "source", path: "/media/still.jpg", mimeType: "image/jpeg" }] },
    { kind: "rating", value: 2 },
    { kind: "technical", duration: null, width: 800, height: 600, frameRate: null, bitRate: null, sampleRate: null, channels: null, codec: null, container: null, format: "jpg" },
  ],
  coverUrl: "/assets/images/image-1/thumb.jpg",
};

const second: UniversalLightboxEntity = {
  ...still,
  id: "image-2",
  title: "Second",
  coverUrl: "/assets/images/image-2/thumb.jpg",
};

const animated: UniversalLightboxEntity = {
  id: "image-video-1",
  kind: "image",
  title: "Animated.webm",
  capabilities: [
    { kind: "files", items: [{ role: "source", path: "/media/animated.webm", mimeType: "video/webm" }] },
  ],
  coverUrl: "/assets/images/image-video-1/thumb.jpg",
};

const animatedWithPreview: UniversalLightboxEntity = {
  ...animated,
  capabilities: [
    {
      kind: "files",
      items: [
        { role: "source", path: "/media/animated.webm", mimeType: "video/webm" },
        { role: "preview", path: "/assets/images/image-video-1/preview.mp4", mimeType: "video/mp4" },
      ],
    },
  ],
};

describe("UniversalLightbox", () => {
  beforeEach(() => {
    globalThis.ResizeObserver = class {
      observe() {}
      disconnect() {}
      unobserve() {}
    };
    Object.defineProperty(HTMLImageElement.prototype, "naturalWidth", { configurable: true, value: 800 });
    Object.defineProperty(HTMLImageElement.prototype, "naturalHeight", { configurable: true, value: 600 });
    Object.defineProperty(window, "matchMedia", {
      configurable: true,
      writable: true,
      value: vi.fn().mockImplementation((query: string) => ({
        matches: false,
        media: query,
        onchange: null,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        addListener: vi.fn(),
        removeListener: vi.fn(),
        dispatchEvent: vi.fn(),
      })),
    });
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(new Response(JSON.stringify({ state: "ready" }))),
    );
  });

  it("closes with Escape", async () => {
    const onClose = vi.fn();
    render(UniversalLightboxHarness, {
      props: { entities: [still], initialIndex: 0, onClose },
    });

    await fireEvent.keyDown(window, { key: "Escape" });

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("portals the overlay to the document body so it covers the app shell", () => {
    const { container } = render(UniversalLightboxHarness, {
      props: { entities: [still], initialIndex: 0, onClose: vi.fn() },
    });

    const overlay = document.body.querySelector(".universal-lightbox");
    expect(overlay).toBeInTheDocument();
    expect(container.querySelector(".universal-lightbox")).toBeNull();
  });

  it("moves through entities with arrow and vim keys", async () => {
    const onIndexChange = vi.fn();
    render(UniversalLightboxHarness, {
      props: { entities: [still, second], initialIndex: 0, onClose: vi.fn(), onIndexChange },
    });

    await fireEvent.keyDown(window, { key: "ArrowRight" });
    await fireEvent.keyDown(window, { key: "h" });

    expect(onIndexChange).toHaveBeenCalledWith(1);
    expect(onIndexChange).toHaveBeenCalledWith(0);
  });

  it("reports rating hotkeys without rendering inline detail controls", async () => {
    const onRatingChange = vi.fn();
    render(UniversalLightboxHarness, {
      props: { entities: [still], initialIndex: 0, onClose: vi.fn(), onRatingChange },
    });

    await fireEvent.keyDown(window, { key: "5" });
    await fireEvent.keyDown(window, { key: "i" });

    expect(onRatingChange).toHaveBeenCalledWith("image-1", 5);
    expect(screen.queryByRole("button", { name: "Details" })).not.toBeInTheDocument();
    expect(screen.queryByText("Dimensions")).not.toBeInTheDocument();
    expect(screen.queryByText("800 × 600")).not.toBeInTheDocument();
  });

  it("can suppress rating controls and hotkeys for read-only previews", async () => {
    const onRatingChange = vi.fn();
    render(UniversalLightboxHarness, {
      props: {
        entities: [still],
        initialIndex: 0,
        onClose: vi.fn(),
        onRatingChange,
        showRatingControls: false,
      },
    });

    await fireEvent.keyDown(window, { key: "5" });

    expect(onRatingChange).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "Rate 1" })).not.toBeInTheDocument();
    expect(screen.queryByText("1-5 rate")).not.toBeInTheDocument();
  });

  it("uses provided detail content as the info back page", async () => {
    render(UniversalLightboxDetailsHarness, {
      props: {
        entities: [still],
        initialIndex: 0,
        onClose: vi.fn(),
      },
    });

    await fireEvent.keyDown(window, { key: "i" });

    expect(screen.getByText("Details for Still")).toBeInTheDocument();
    expect(screen.queryByText("Dimensions")).not.toBeInTheDocument();
  });

  it("renders video-capable image entities through Vidstack minimal mode", async () => {
    render(UniversalLightboxHarness, {
      props: { entities: [animatedWithPreview], initialIndex: 0, onClose: vi.fn() },
    });

    await waitFor(() => {
      expect(screen.getByTestId("vidstack-video-player")).toBeInTheDocument();
    });
    expect(screen.queryByRole("button", { name: "Player settings" })).not.toBeInTheDocument();
  });

  it("autoplays video-capable items muted and repeats in the lightbox", async () => {
    render(UniversalLightboxHarness, {
      props: { entities: [animatedWithPreview], initialIndex: 0, onClose: vi.fn() },
    });

    await waitFor(() => {
      expect(screen.getByTestId("vidstack-video-player")).toBeInTheDocument();
    });
    expect(screen.getByRole("button", { name: "Unmute" })).toBeInTheDocument();

    const source = await readFile("src/lib/components/UniversalLightbox.svelte", "utf8");
    expect(source).toContain("autoRepeat");
  });

  it("uses image-video originals in the full-quality lightbox even without generated previews", async () => {
    render(UniversalLightboxHarness, {
      props: { entities: [animated], initialIndex: 0, onClose: vi.fn() },
    });

    await waitFor(() => {
      expect(screen.getByTestId("vidstack-video-player")).toBeInTheDocument();
    });
  });

  it("requests original-first video sources for full-quality lightbox playback", async () => {
    const source = await readFile("src/lib/components/UniversalLightbox.svelte", "utf8");

    expect(source).toContain("buildLightboxVideoSources(current, { preferOriginal: true })");
  });

  it("preloads neighboring lightbox media for smoother navigation", async () => {
    render(UniversalLightboxHarness, {
      props: { entities: [still, second, animatedWithPreview], initialIndex: 1, onClose: vi.fn() },
    });

    await tick();

    const links = Array.from(
      document.head.querySelectorAll<HTMLLinkElement>("link[data-lightbox-preload]"),
    ).map((link) => ({
      rel: link.getAttribute("rel"),
      as: link.getAttribute("as"),
      href: link.getAttribute("href"),
    }));

    expect(links).toEqual([
      { rel: "preload", as: "image", href: "/api/entities/image-1/files/source" },
      { rel: "preload", as: "image", href: "/assets/images/image-video-1/thumb.jpg" },
      { rel: "prefetch", as: "video", href: "/api/entities/image-video-1/files/preview" },
    ]);
  });

  it("sizes embedded minimal videos with a real responsive lightbox frame", async () => {
    const source = await readFile("src/lib/components/UniversalLightbox.svelte", "utf8");

    expect(source).toContain(".media-frame :global([data-testid=\"vidstack-video-player\"])");
    expect(source).toContain("aspect-ratio: 16 / 9;");
    expect(source).toContain("max-height: calc(100dvh - 10rem);");
  });

  it("keeps lightbox navigation in the bars instead of overlaying media", async () => {
    const source = await readFile("src/lib/components/UniversalLightbox.svelte", "utf8");

    expect(source).not.toContain("nav-button is-prev");
    expect(source).not.toContain("nav-button is-next");
    expect(source).toContain("bottom-controls");
  });
});
