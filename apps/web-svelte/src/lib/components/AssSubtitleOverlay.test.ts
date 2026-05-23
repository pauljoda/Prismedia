import { render, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import AssSubtitleOverlay from "./AssSubtitleOverlay.svelte";

const { fetchVideoSubtitleSource, jassubCtor, destroy, loadJassub } = vi.hoisted(() => {
  const destroy = vi.fn();
  const jassubCtor = vi.fn(function MockJassub() {
    return { destroy };
  });
  return {
    fetchVideoSubtitleSource: vi.fn(),
    jassubCtor,
    destroy,
    loadJassub: vi.fn(),
  };
});

vi.mock("$lib/player/video-subtitles", () => ({
  fetchVideoSubtitleSource,
}));

vi.mock("$lib/vendor/load-jassub", () => ({
  loadJassub,
}));

describe("AssSubtitleOverlay", () => {
  beforeEach(() => {
    fetchVideoSubtitleSource.mockReset();
    fetchVideoSubtitleSource.mockResolvedValue("[Script Info]\nTitle: Test");
    jassubCtor.mockClear();
    destroy.mockClear();
    loadJassub.mockReset();
    loadJassub.mockResolvedValue(jassubCtor);
  });

  it("boots once the video element becomes available after initial render", async () => {
    const { rerender } = render(AssSubtitleOverlay, {
      props: {
        videoEl: null,
        sourceUrl: "/assets/videos/video-1/subtitles/track-1.ass",
      },
    });

    expect(jassubCtor).not.toHaveBeenCalled();

    const wrapper = document.createElement("div");
    const video = document.createElement("video");
    wrapper.append(video);
    document.body.append(wrapper);

    await rerender({
      videoEl: video,
      sourceUrl: "/assets/videos/video-1/subtitles/track-1.ass",
    });

    await waitFor(() => {
      expect(fetchVideoSubtitleSource).toHaveBeenCalledWith(
        "/assets/videos/video-1/subtitles/track-1.ass",
      );
      expect(jassubCtor).toHaveBeenCalledTimes(1);
    });

    expect(jassubCtor).toHaveBeenCalledWith(
      expect.objectContaining({
        video,
        workerUrl: "/jassub/jassub-worker.js",
        wasmUrl: "/jassub/jassub-worker.wasm",
      }),
    );
  });
});
