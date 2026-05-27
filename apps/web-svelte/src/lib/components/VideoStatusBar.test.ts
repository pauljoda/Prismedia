import { render, screen } from "@testing-library/svelte";
import { describe, expect, it } from "vitest";
import VideoStatusBar from "./VideoStatusBar.svelte";

describe("VideoStatusBar", () => {
  it("shows stream quality and instrumentation for HLS", () => {
    render(VideoStatusBar, {
      props: {
        activePlaybackLabel: "HLS",
        bandwidthLabel: "8.0 Mbps",
        bufferAhead: 4.25,
        droppedFrames: 2,
        mode: "hls",
        qualityLabel: "1080p",
        showControls: true,
      },
    });

    expect(screen.getByText("HLS")).toBeInTheDocument();
    expect(screen.getByTestId("playback-quality-chip")).toHaveTextContent("1080p");
    expect(screen.getByText("8.0 Mbps")).toBeInTheDocument();
    expect(screen.getByText("4.3s")).toBeInTheDocument();
    expect(screen.getByText("2")).toBeInTheDocument();
  });

  it("hides quality and ABR details for direct playback", () => {
    render(VideoStatusBar, {
      props: {
        activePlaybackLabel: "Direct",
        bandwidthLabel: "-",
        bufferAhead: 0,
        mode: "direct",
        qualityLabel: "Source",
        showControls: true,
      },
    });

    expect(screen.queryByTestId("playback-quality-chip")).not.toBeInTheDocument();
    expect(screen.queryByText("ABR")).not.toBeInTheDocument();
  });
});
