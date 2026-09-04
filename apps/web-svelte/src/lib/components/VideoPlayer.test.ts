import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import VideoPlayer from "./VideoPlayer.svelte";
import type {
  SubtitleAppearance,
  SubtitlePreferenceTerm,
  VideoSubtitleTrack,
} from "$lib/player/subtitle-types";

vi.mock("vidstack/player", () => ({}));
vi.mock("vidstack/player/layouts", () => ({}));
vi.mock("vidstack/player/ui", () => ({}));
vi.mock("vidstack", () => ({
  isHLSProvider: () => false,
}));

const subtitleDefaults: {
  autoEnable: boolean;
  preferredTerms: SubtitlePreferenceTerm[];
  appearance: SubtitleAppearance;
} = {
  autoEnable: true,
  preferredTerms: [
    { term: "English", weight: 100 },
    { term: "Eng", weight: 80 },
  ],
  appearance: {
    style: "stylized",
    fontScale: 1,
    positionPercent: 88,
    opacity: 1,
  },
};

const googleCastSenderUrl =
  "https://www.gstatic.com/cv/js/sender/v1/cast_sender.js?loadCastFramework=1";

function makeTrack(
  id: string,
  language: string,
  videoId = "video-1",
): VideoSubtitleTrack {
  return {
    id,
    videoId,
    language,
    label: null,
    format: "vtt",
    source: "embedded",
    sourceFormat: "vtt",
    isDefault: false,
    url: `/api/videos/${videoId}/subtitles/${id}`,
    sourceUrl: null,
    createdAt: "2026-04-23T00:00:00.000Z",
  };
}

describe("VideoPlayer", () => {
  beforeEach(() => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ state: "ready", renditions: [] }), {
          headers: { "Content-Type": "application/json" },
        }),
      ),
    );
    window.localStorage?.removeItem?.("prismedia:subtitle-appearance");
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
  });

  afterEach(() => {
    document
      .querySelectorAll(`script[src="${googleCastSenderUrl}"]`)
      .forEach((script) => script.remove());
    Reflect.deleteProperty(window, "__onGCastApiAvailable");
    Reflect.deleteProperty(window, "chrome");
    Reflect.deleteProperty(window, "cast");
    Reflect.deleteProperty(HTMLElement.prototype, "requestFullscreen");
    vi.unstubAllGlobals();
  });

  it("shows the initial playback position on the seek bar before media events arrive", () => {
    render(VideoPlayer, {
      props: {
        src: "/api/videos/video-1/hls/master.m3u8",
        defaultPlaybackMode: "hls",
        duration: 100,
        initialTime: 25,
      },
    });

    expect(screen.getByTestId("video-progress-track")).toHaveStyle(
      "--prismedia-slider-fill: 25%",
    );
    expect(screen.getByText("0:25 / 1:40")).toBeInTheDocument();
  });

  it("leaves focused controls and consumed keys to their UI owner", async () => {
    const { container } = render(VideoPlayer, {
      src: "/api/videos/video-1/hls/master.m3u8",
      defaultPlaybackMode: "hls",
      duration: 100,
      initialTime: 25,
    });
    const tab = document.createElement("button");
    tab.setAttribute("role", "tab");
    tab.textContent = "Metadata";
    container.append(tab);

    await fireEvent.keyDown(tab, { key: "ArrowRight" });
    expect(screen.getByText("0:25 / 1:40")).toBeInTheDocument();

    const consumed = new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true, cancelable: true });
    consumed.preventDefault();
    await fireEvent(window, consumed);
    expect(screen.getByText("0:25 / 1:40")).toBeInTheDocument();

    await fireEvent.keyDown(window, { key: "ArrowRight" });
    expect(screen.getByText("0:30 / 1:40")).toBeInTheDocument();
  });

  it("auto-selects the preferred subtitle track when unlocked", async () => {
    const onActiveSubtitleTrackIdChange = vi.fn();

    render(VideoPlayer, {
      props: {
        subtitleTracks: [
          makeTrack("track-ja", "ja"),
          makeTrack("track-en", "en"),
        ],
        subtitleDefaults,
        activeSubtitleTrackId: null,
        subtitleChoiceLocked: false,
        onActiveSubtitleTrackIdChange,
      },
    });

    await waitFor(() => {
      expect(onActiveSubtitleTrackIdChange).toHaveBeenCalledWith("track-en");
    });
  });

  it("auto-selects after a stale subtitle lock is cleared without remounting", async () => {
    const onActiveSubtitleTrackIdChange = vi.fn();

    const { rerender } = render(VideoPlayer, {
      props: {
        subtitleTracks: [
          makeTrack("track-ja", "ja"),
          makeTrack("track-en", "en"),
        ],
        subtitleDefaults,
        activeSubtitleTrackId: null,
        subtitleChoiceLocked: true,
        onActiveSubtitleTrackIdChange,
      },
    });

    expect(onActiveSubtitleTrackIdChange).not.toHaveBeenCalled();

    await rerender({
      subtitleTracks: [
        makeTrack("track-ja", "ja"),
        makeTrack("track-en", "en"),
      ],
      subtitleDefaults,
      activeSubtitleTrackId: null,
      subtitleChoiceLocked: false,
      onActiveSubtitleTrackIdChange,
    });

    await waitFor(() => {
      expect(onActiveSubtitleTrackIdChange).toHaveBeenCalledWith("track-en");
    });
  });

  it("re-applies defaults for a new video after the parent clears a prior lock", async () => {
    const onActiveSubtitleTrackIdChange = vi.fn();

    const { rerender } = render(VideoPlayer, {
      props: {
        src: "/api/video-stream/video-1/hls2/master.m3u8",
        subtitleTracks: [makeTrack("track-en-1", "en", "video-1")],
        subtitleDefaults,
        activeSubtitleTrackId: null,
        subtitleChoiceLocked: true,
        onActiveSubtitleTrackIdChange,
      },
    });

    await waitFor(() => {
      expect(onActiveSubtitleTrackIdChange).not.toHaveBeenCalled();
    });

    await rerender({
      src: "/api/video-stream/video-2/hls2/master.m3u8",
      subtitleTracks: [makeTrack("track-en-2", "en", "video-2")],
      subtitleDefaults,
      activeSubtitleTrackId: null,
      subtitleChoiceLocked: false,
      onActiveSubtitleTrackIdChange,
    });

    await waitFor(() => {
      expect(onActiveSubtitleTrackIdChange).toHaveBeenCalledWith("track-en-2");
    });
  });

  it("renders the Vidstack playback shell with active status, settings, and cast controls", async () => {
    render(VideoPlayer, {
      props: {
        src: "/api/video-stream/video-1/hls2/master.m3u8",
        directSrc: "/api/video-stream/video-1/source",
        defaultPlaybackMode: "hls",
        streamMethod: "transcode",
        resolutionLabel: "4K",
        dynamicRangeLabel: "Dolby Vision",
        audioFormatLabel: "Dolby Atmos 7.1",
      },
    });

    expect(screen.getByTestId("vidstack-video-player")).toBeInTheDocument();
    expect(screen.getByTestId("playback-method-chip")).toHaveTextContent("Transcoding");
    expect(screen.getByTestId("resolution-chip")).toHaveTextContent("4K");
    expect(screen.getByTestId("dynamic-range-chip")).toHaveTextContent("Dolby Vision");
    expect(screen.getByTestId("audio-format-chip")).toHaveTextContent("Dolby Atmos 7.1");
    expect(screen.getByRole("button", { name: "Cast" })).toBeInTheDocument();
    const settingsButton = screen.getByRole("button", { name: "Player settings" });
    await fireEvent.click(settingsButton);
    expect(screen.getByRole("dialog", { name: "Player settings menu" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Quality/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Audio/ })).toBeInTheDocument();

    await fireEvent.click(settingsButton);
    await waitFor(() => {
      expect(screen.queryByRole("dialog", { name: "Player settings menu" })).not.toBeInTheDocument();
    });
  });

  it("labels an adaptive rendition as transcoding when the negotiated source was direct", () => {
    render(VideoPlayer, {
      props: {
        src: "/api/playback/videos/video-1/hls/v/master.m3u8",
        directSrc: "/api/playback/videos/video-1/stream",
        defaultPlaybackMode: "hls",
        streamMethod: "direct",
      },
    });

    expect(screen.getByTestId("playback-method-chip")).toHaveTextContent("Transcoding");
    expect(screen.getByTestId("playback-method-chip")).not.toHaveTextContent("Direct Stream");
  });

  it("hides the full playback chrome in minimal mode", async () => {
    render(VideoPlayer, {
      props: {
        directSrc: "/api/entities/image-video-1/files/source",
        codec: "vp9",
        defaultPlaybackMode: "direct",
        chrome: "minimal",
        enableKeyboardShortcuts: false,
      },
    });

    await waitFor(() => {
      expect(screen.getByTestId("vidstack-video-player")).toBeInTheDocument();
    });
    expect(screen.queryByRole("button", { name: "Player settings" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Fullscreen" })).not.toBeInTheDocument();
  });

  it("can mount autoplay looping media muted for lightbox playback", async () => {
    render(VideoPlayer, {
      props: {
        directSrc: "/fixtures/lightbox/animated-loop.webm",
        defaultPlaybackMode: "direct",
        chrome: "minimal",
        autoPlay: true,
        autoRepeat: true,
        initialMuted: true,
      },
    });

    await waitFor(() => {
      expect(document.querySelector("media-player")).toBeInTheDocument();
    });
    expect(document.querySelector("media-player")?.getAttribute("muted")).not.toBeNull();
    expect(document.querySelector("media-player")?.getAttribute("loop")).not.toBeNull();
  });

  it("derives the resolution badge from source dimensions with exact pixels in the tooltip", () => {
    render(VideoPlayer, {
      props: {
        src: "/api/videos/video-1/hls/master.m3u8",
        defaultPlaybackMode: "hls",
        sourceWidth: 3840,
        sourceHeight: 1920,
      },
    });

    const chip = screen.getByTestId("resolution-chip");
    expect(chip).toHaveTextContent("4K");
    expect(chip).toHaveAttribute("title", "3840x1920");
  });

  it("shows server-provided audio tracks when the HLS provider exposes only one muxed track", async () => {
    const onAudioTrackChange = vi.fn();

    render(VideoPlayer, {
      props: {
        src: "/api/videos/video-1/hls/master.m3u8",
        defaultPlaybackMode: "hls",
        audioTrackOptions: [
          { id: "audio-1", streamIndex: 1, label: "Spanish", selected: false },
          { id: "audio-2", streamIndex: 2, label: "English · Default", selected: true },
        ],
        onAudioTrackChange,
      },
    });

    const settingsButton = screen.getByRole("button", { name: "Player settings" });
    await fireEvent.click(settingsButton);
    await fireEvent.click(screen.getByRole("button", { name: /Audio/ }));

    expect(screen.getByRole("button", { name: /English · Default/ })).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: /Spanish/ }));

    expect(onAudioTrackChange).toHaveBeenCalledWith(1);
  });

  it("uses adaptive playback instead of direct playback when backend audio tracks need selection", async () => {
    render(VideoPlayer, {
      props: {
        src: "/api/playback/videos/video-1/hls/master.m3u8?audioStreamIndex=2",
        directSrc: "/api/playback/videos/video-1/stream",
        codec: "h264",
        defaultPlaybackMode: "direct",
        audioTrackOptions: [
          { id: "audio-1", streamIndex: 1, label: "Italian", selected: false },
          { id: "audio-2", streamIndex: 2, label: "English", selected: true },
        ],
      },
    });

    const settingsButton = screen.getByRole("button", { name: "Player settings" });
    await fireEvent.click(settingsButton);
    await fireEvent.click(screen.getByRole("button", { name: /Quality/ }));

    expect(screen.queryByRole("button", { name: /^Direct$/ })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Auto/ })).toBeInTheDocument();
  });

  it("hides cast controls when the library setting disables them", () => {
    render(VideoPlayer, {
      props: {
        src: "/api/video-stream/video-1/hls2/master.m3u8",
        defaultPlaybackMode: "hls",
        showCastControls: false,
      },
    });

    expect(screen.queryByRole("button", { name: "Cast" })).not.toBeInTheDocument();
  });

  it("shows an unavailable notice when cast has no request target", async () => {
    render(VideoPlayer, {
      props: {
        src: "/api/videos/video-1/hls/master.m3u8",
        defaultPlaybackMode: "hls",
      },
    });

    await waitFor(() => {
      expect(document.querySelector("media-player")).toBeInTheDocument();
    });

    Object.defineProperty(document.querySelector("media-player"), "remoteControl", {
      configurable: true,
      value: {},
    });

    await fireEvent.click(screen.getByRole("button", { name: "Cast" }));

    expect(screen.getByText("Casting is not available for this player.")).toBeInTheDocument();
  });

  it("loads the Google Cast sender framework and requests Cast once available", async () => {
    const requestGoogleCast = vi.fn();
    render(VideoPlayer, {
      props: {
        src: "/api/videos/video-1/hls/master.m3u8",
        defaultPlaybackMode: "hls",
      },
    });

    await waitFor(() => {
      expect(document.querySelector("media-player")).toBeInTheDocument();
    });

    Object.defineProperty(document.querySelector("media-player"), "remoteControl", {
      configurable: true,
      value: { requestGoogleCast },
    });

    await waitFor(() => {
      expect(document.querySelector(`script[src="${googleCastSenderUrl}"]`)).toBeInTheDocument();
    });

    (window as unknown as { __onGCastApiAvailable?: (available: boolean) => void })
      .__onGCastApiAvailable?.(true);

    await fireEvent.click(screen.getByRole("button", { name: "Cast" }));

    await waitFor(() => {
      expect(requestGoogleCast).toHaveBeenCalled();
    });
  });

  it("shows an unavailable notice when Google Cast cannot load", async () => {
    const requestGoogleCast = vi.fn();
    render(VideoPlayer, {
      props: {
        src: "/api/videos/video-1/hls/master.m3u8",
        defaultPlaybackMode: "hls",
      },
    });

    await waitFor(() => {
      expect(document.querySelector("media-player")).toBeInTheDocument();
    });

    Object.defineProperty(document.querySelector("media-player"), "remoteControl", {
      configurable: true,
      value: { requestGoogleCast },
    });

    await waitFor(() => {
      expect(document.querySelector(`script[src="${googleCastSenderUrl}"]`)).toBeInTheDocument();
    });

    (window as unknown as { __onGCastApiAvailable?: (available: boolean) => void })
      .__onGCastApiAvailable?.(false);

    await fireEvent.click(screen.getByRole("button", { name: "Cast" }));

    expect(requestGoogleCast).not.toHaveBeenCalled();
    await waitFor(() => {
      expect(screen.getByText("Google Cast is not available for this browser.")).toBeInTheDocument();
    });
  });

  it("toggles captions from settings without showing a non-error notice", async () => {
    const onActiveSubtitleTrackIdChange = vi.fn();
    render(VideoPlayer, {
      props: {
        subtitleTracks: [makeTrack("track-en", "en")],
        subtitleDefaults,
        activeSubtitleTrackId: null,
        subtitleChoiceLocked: true,
        onActiveSubtitleTrackIdChange,
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Player settings" }));
    await fireEvent.click(screen.getByRole("button", { name: /Captions/ }));
    await fireEvent.click(screen.getByRole("button", { name: /English/ }));

    expect(onActiveSubtitleTrackIdChange).toHaveBeenCalledWith("track-en");
    expect(screen.queryByText("Captions on.")).not.toBeInTheDocument();
  });

  it("uses the player row subtitle button only for the transcript sidecar", async () => {
    const onActiveSubtitleTrackIdChange = vi.fn();
    const onTranscriptSidecarToggle = vi.fn();

    render(VideoPlayer, {
      props: {
        subtitleTracks: [makeTrack("track-en", "en")],
        subtitleDefaults,
        activeSubtitleTrackId: "track-en",
        subtitleChoiceLocked: true,
        onActiveSubtitleTrackIdChange,
        onTranscriptSidecarToggle,
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Show transcript sidecar" }));

    expect(onTranscriptSidecarToggle).toHaveBeenCalledOnce();
    expect(onActiveSubtitleTrackIdChange).not.toHaveBeenCalled();
  });

  it("shows a notice when fullscreen cannot be entered", async () => {
    Object.defineProperty(HTMLElement.prototype, "requestFullscreen", {
      configurable: true,
      value: vi.fn().mockRejectedValue(new Error("Fullscreen blocked")),
    });

    render(VideoPlayer, {
      props: {
        src: "/api/videos/video-1/hls/master.m3u8",
        defaultPlaybackMode: "hls",
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Fullscreen" }));

    await waitFor(() => {
      expect(screen.getByText("Fullscreen is not available for this browser.")).toBeInTheDocument();
    });
  });

});
