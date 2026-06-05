import { render, screen } from "@testing-library/svelte";
import { describe, expect, it } from "vitest";
import VideoStatusBar from "./VideoStatusBar.svelte";

describe("VideoStatusBar", () => {
  it("shows viewer-friendly content badges and the delivery method", () => {
    render(VideoStatusBar, {
      props: {
        playbackMethod: "transcode",
        methodLabel: "Transcoding",
        methodHint: "Converting the video for your browser",
        methodDetail: "1080p · H.264 · SDR",
        resolutionLabel: "4K",
        videoDetail: "3840x2160 · HEVC",
        dynamicRangeLabel: "Dolby Vision",
        audioFormatLabel: "Dolby Atmos 7.1",
        bufferSeconds: 12.42,
        showControls: true,
      },
    });

    expect(screen.getByTestId("playback-method-chip")).toHaveTextContent("Transcoding");
    expect(screen.getByTestId("resolution-chip")).toHaveTextContent("4K");
    expect(screen.getByTestId("dynamic-range-chip")).toHaveTextContent("Dolby Vision");
    expect(screen.getByTestId("audio-format-chip")).toHaveTextContent("Dolby Atmos 7.1");
    expect(screen.getByTestId("buffer-chip")).toHaveTextContent("12.4s");
  });

  it("omits the HDR badge for SDR sources and labels direct play", () => {
    render(VideoStatusBar, {
      props: {
        playbackMethod: "direct",
        methodLabel: "Direct Play",
        resolutionLabel: "1080p",
        dynamicRangeLabel: null,
        audioFormatLabel: "Stereo AAC",
        showControls: true,
      },
    });

    expect(screen.getByTestId("playback-method-chip")).toHaveTextContent("Direct Play");
    expect(screen.queryByTestId("dynamic-range-chip")).not.toBeInTheDocument();
  });
});
