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
