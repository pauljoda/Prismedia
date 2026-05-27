import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import VideoTransportControls from "./VideoTransportControls.svelte";

describe("VideoTransportControls", () => {
  it("dispatches transport actions", async () => {
    const onSeek = vi.fn();
    const onTogglePlay = vi.fn();

    render(VideoTransportControls, {
      props: {
        onSeek,
        onTogglePlay,
      },
    });

    await fireEvent.click(screen.getByLabelText("Skip back 10s"));
    await fireEvent.click(screen.getByLabelText("Play"));
    await fireEvent.click(screen.getByLabelText("Skip forward 10s"));

    expect(onSeek).toHaveBeenNthCalledWith(1, -10);
    expect(onTogglePlay).toHaveBeenCalledTimes(1);
    expect(onSeek).toHaveBeenNthCalledWith(2, 10);
  });

  it("labels the primary button as pause while playing", () => {
    render(VideoTransportControls, {
      props: {
        playing: true,
        onSeek: vi.fn(),
        onTogglePlay: vi.fn(),
      },
    });

    expect(screen.getByLabelText("Pause")).toBeInTheDocument();
  });
});
