import { readFile } from "node:fs/promises";
import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import VideoTimeline from "./VideoTimeline.svelte";

function baseProps() {
  return {
    bufferedProgressPercent: 42,
    fullChrome: true,
    markersCount: 1,
    onHover: vi.fn(),
    onHoverEnd: vi.fn(),
    playbackProgressPercent: 25,
    showControls: true,
    timelineHover: {
      chapterTitle: "Opening",
      markerTitles: [],
      percent: 25,
      time: 12,
    },
    timelinePreviewFrame: {
      url: "/tiles/0.jpg",
      start: 10,
      end: 15,
      x: 0,
      y: 0,
      width: 160,
      height: 90,
    },
    timelinePreviewSpriteDims: { width: 320, height: 90 },
  };
}

describe("VideoTimeline", () => {
  it("renders native playback progress and hover preview", () => {
    render(VideoTimeline, {
      props: baseProps(),
    });

    const track = screen.getByTestId("video-progress-track");
    expect(track).toHaveStyle("--prismedia-slider-fill: 25%");
    expect(track).toHaveStyle("--prismedia-buffer-progress: 42%");
    expect(screen.getByTestId("timeline-trickplay-preview")).toBeInTheDocument();
    expect(screen.getByText("Opening")).toBeInTheDocument();
  });

  it("suppresses VidStack's duration-driven fill so the brass cannot overrun the playhead", async () => {
    // VidStack positions its own track-fill (and per-chapter fill) from the media element's raw
    // duration, which for a still-growing on-demand HLS playlist is only the produced-so-far length
    // — so it paints the fill to ~100% and the brass runs ahead of the real playhead. The visible
    // fill must come solely from `.is-played`, driven by our authoritative playbackProgressPercent.
    const source = await readFile("src/lib/components/VideoTimeline.svelte", "utf8");

    // VidStack's duration-driven fill is made transparent...
    expect(source).toContain("--media-slider-track-fill-bg: transparent;");
    // ...and the per-chapter fill paints nothing (its width is VidStack/duration-driven).
    expect(source).toMatch(/\.video-slider-track-fill \{[^}]*background: transparent;/s);
    // The only visible played fill is `.is-played`, using our own gradient + authoritative width.
    expect(source).toMatch(
      /\.video-slider-native-progress\.is-played \{[^}]*background: var\(--prismedia-fill-gradient\);[^}]*width: var\(--prismedia-slider-fill, 0%\);/s,
    );
  });

  it("emits hover lifecycle callbacks", async () => {
    const onHover = vi.fn();
    const onHoverEnd = vi.fn();
    render(VideoTimeline, {
      props: {
        ...baseProps(),
        onHover,
        onHoverEnd,
      },
    });

    const track = screen.getByTestId("video-progress-track");
    vi.spyOn(track, "getBoundingClientRect").mockReturnValue({
      bottom: 0,
      height: 10,
      left: 10,
      right: 110,
      top: 0,
      width: 100,
      x: 10,
      y: 0,
      toJSON: () => ({}),
    });

    const pointerMove = new Event("pointermove", { bubbles: true });
    Object.defineProperty(pointerMove, "clientX", { value: 60 });

    await fireEvent(track, pointerMove);
    await fireEvent.pointerLeave(track);

    expect(onHover).toHaveBeenCalledWith(60, expect.objectContaining({ left: 10, width: 100 }));
    expect(onHoverEnd).toHaveBeenCalled();
  });
});
