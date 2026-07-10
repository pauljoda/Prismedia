import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import FilmStrip from "./FilmStrip.svelte";

vi.mock("@lucide/svelte", () => ({
  ChevronLeft: vi.fn(),
  ChevronRight: vi.fn(),
}));

vi.mock("@prismedia/ui-svelte", () => ({
  loadTrickplayFrames: vi.fn().mockResolvedValue([
    { start: 0, end: 50, x: 0, y: 0, width: 160, height: 90, url: "/0.jpg" },
    { start: 50, end: 100, x: 160, y: 0, width: 160, height: 90, url: "/0.jpg" },
  ]),
  findFrameAtTime: vi.fn(() => 0),
  timeToTrackPosition: vi.fn((_frames, time, frameWidth) => (time / 50) * frameWidth),
}));

describe("FilmStrip", () => {
  let matchMediaMatches = false;

  beforeEach(() => {
    matchMediaMatches = false;
    vi.stubGlobal("requestAnimationFrame", vi.fn(() => 1));
    vi.stubGlobal("cancelAnimationFrame", vi.fn());
    Object.defineProperty(window, "matchMedia", {
      configurable: true,
      writable: true,
      value: vi.fn().mockImplementation((query: string) => ({
        matches: matchMediaMatches,
        media: query,
        onchange: null,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        addListener: vi.fn(),
        removeListener: vi.fn(),
        dispatchEvent: vi.fn(),
      })),
    });
    HTMLElement.prototype.setPointerCapture = vi.fn();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("commits a dragged scrub position only after the pointer is released", async () => {
    const onSeek = vi.fn();

    const { container } = render(FilmStrip, {
      props: {
        playlistUrl: "/trickplay.m3u8",
        videoEl: null,
        currentTime: 10,
        duration: 100,
        onSeek,
      },
    });

    await screen.findByRole("button", { name: "Previous frame" });
    const scrubber = container.querySelector(".touch-pan-y") as HTMLElement;
    expect(scrubber).toBeTruthy();

    await fireEvent(scrubber, pointerEvent("pointerdown", 200));
    await fireEvent(scrubber, pointerEvent("pointermove", 108));

    expect(onSeek).not.toHaveBeenCalled();

    await fireEvent(scrubber, pointerEvent("pointerup", 108));

    await waitFor(() => {
      expect(onSeek).toHaveBeenCalledWith(60);
    });
  });

  it("commits horizontal wheel scrubbing after scrolling idles", async () => {
    matchMediaMatches = true;
    const onSeek = vi.fn();

    const { container } = render(FilmStrip, {
      props: {
        playlistUrl: "/trickplay.m3u8",
        videoEl: null,
        currentTime: 10,
        duration: 100,
        onSeek,
      },
    });

    await screen.findByRole("button", { name: "Previous frame" });
    const scrubber = container.querySelector(".touch-pan-y") as HTMLElement;

    await fireEvent.wheel(scrubber, { deltaX: 92 });

    expect(onSeek).not.toHaveBeenCalled();

    await waitFor(() => {
      expect(onSeek).toHaveBeenCalledWith(60);
    });
  });

  it("leaves vertical wheel input for the page to scroll", async () => {
    matchMediaMatches = true;
    const onSeek = vi.fn();
    const onStripInteractionChange = vi.fn();

    const { container } = render(FilmStrip, {
      props: {
        playlistUrl: "/trickplay.m3u8",
        videoEl: null,
        currentTime: 10,
        duration: 100,
        onSeek,
        onStripInteractionChange,
      },
    });

    await screen.findByRole("button", { name: "Previous frame" });
    const scrubber = container.querySelector(".touch-pan-y") as HTMLElement;
    const verticalWheel = new WheelEvent("wheel", {
      bubbles: true,
      cancelable: true,
      deltaX: 4,
      deltaY: 92,
    });

    scrubber.dispatchEvent(verticalWheel);

    expect(verticalWheel.defaultPrevented).toBe(false);
    expect(onStripInteractionChange).not.toHaveBeenCalled();
    expect(onSeek).not.toHaveBeenCalled();
  });

  it("lets marker labels seek directly without starting a strip drag", async () => {
    const onSeek = vi.fn();
    const onStripInteractionChange = vi.fn();

    render(FilmStrip, {
      props: {
        playlistUrl: "/trickplay.m3u8",
        videoEl: null,
        currentTime: 10,
        duration: 100,
        onSeek,
        onStripInteractionChange,
        markers: [{ id: "credits", title: "Credits", time: 75 }],
      },
    });

    const marker = await screen.findByRole("button", { name: "Seek to Credits" });
    await fireEvent(marker, pointerEvent("pointerdown", 0));
    await fireEvent.click(marker);

    expect(onStripInteractionChange).not.toHaveBeenCalled();
    expect(onSeek).toHaveBeenCalledWith(75);
  });
});

function pointerEvent(type: string, clientX: number) {
  const event = new Event(type, { bubbles: true, cancelable: true });
  Object.defineProperty(event, "clientX", { value: clientX });
  Object.defineProperty(event, "pointerId", { value: 1 });
  return event;
}
