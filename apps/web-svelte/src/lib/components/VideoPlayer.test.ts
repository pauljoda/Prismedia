import { readFile } from "node:fs/promises";
import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import VideoPlayer from "./VideoPlayer.svelte";
import type {
  SubtitleAppearance,
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
  preferredLanguages: string;
  appearance: SubtitleAppearance;
} = {
  autoEnable: true,
  preferredLanguages: "en,eng",
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
    expect(screen.getByRole("menu", { name: "Player settings menu" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Quality/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Audio/ })).toBeInTheDocument();

    await fireEvent.click(settingsButton);
    await waitFor(() => {
      expect(screen.queryByRole("menu", { name: "Player settings menu" })).not.toBeInTheDocument();
    });
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

  it("keeps minimal lightbox video clicks and muted state on the native media element", async () => {
    const source = await readFile("src/lib/components/VideoPlayer.svelte", "utf8");

    expect(source).toContain("function mediaElement()");
    expect(source).toContain("scheduleInitialMutedSync()");
    expect(source).toContain("event.stopImmediatePropagation();");
    expect(source).toContain("mediaElement()?.pause();");
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
        src: "/Videos/video-1/master.m3u8?AudioStreamIndex=2",
        directSrc: "/Videos/video-1/stream",
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

  it("lets fullscreen video fill the fullscreen surface while controls remain overlayed", async () => {
    const source = await readFile("src/lib/components/VideoPlayer.svelte", "utf8");

    expect(source).toContain(".prismedia-player-surface:fullscreen");
    expect(source).toContain(".prismedia-player-surface:fullscreen .prismedia-media-engine");
    expect(source).toContain("max-width: none;");
    expect(source).toContain("height: 100dvh;");
  });

  it("keeps VidStack provider media layers from stacking poster below video", async () => {
    const source = await readFile("src/lib/components/VideoPlayer.svelte", "utf8");

    expect(source).toContain(".prismedia-media-engine :global(media-provider)");
    expect(source).toContain("position: absolute;");
    expect(source).toContain("inset: 0;");
    expect(source).toContain(".prismedia-media-engine :global(media-poster img)");
    expect(source).toContain("object-fit: cover;");
    expect(source).toContain("object-position: center;");
    expect(source).toContain(":global(.prismedia-media-engine[data-started] media-poster)");
    expect(source).toContain("visibility: hidden;");
  });

  it("uses VidStack chapter sliders for marker sections instead of custom marker ticks", async () => {
    const source = await readFile("src/lib/components/VideoTimeline.svelte", "utf8");
    const playerSource = await readFile("src/lib/components/VideoPlayer.svelte", "utf8");

    expect(playerSource).toContain('import "vidstack/player/ui";');
    expect(source).toContain("<media-time-slider");
    expect(source).toContain("<media-slider-chapters>");
    expect(playerSource).toContain('kind: "chapters"');
    expect(source).not.toContain('data-testid="video-progress-marker"');
  });

  it("renders playback and buffer progress from the native video clock", async () => {
    const source = await readFile("src/lib/components/VideoTimeline.svelte", "utf8");
    const playerSource = await readFile("src/lib/components/VideoPlayer.svelte", "utf8");

    expect(playerSource).toContain("playbackProgressPercent");
    expect(playerSource).toContain("bufferedProgressPercent");
    expect(source).toContain('style:--prismedia-slider-fill={`${playbackProgressPercent}%`}');
    expect(source).toContain('style:--prismedia-buffer-progress={`${bufferedProgressPercent}%`}');
    expect(source).toContain('class="video-slider-native-progress is-buffered"');
    expect(source).toContain('class="video-slider-native-progress is-played"');
    expect(source).toContain("left: var(--prismedia-slider-fill, var(--slider-fill, 0%));");
  });

  it("keeps minimal lightbox progress visible at the bottom edge", async () => {
    const source = await readFile("src/lib/components/VideoTimeline.svelte", "utf8");

    expect(source).toContain('!fullChrome && "is-minimal-progress"');
    expect(source).toContain('!fullChrome || showControls ? "opacity-100" : "opacity-0"');
    expect(source).toContain("{#if fullChrome && timelineHover}");
    expect(source).toContain(".video-time-slider.is-minimal-progress");
    expect(source).toContain("background: rgba(255, 255, 255, 0.18)");
    expect(source).toContain("bottom: 0;");
  });

  it("keeps VidStack controls synchronized with the native video element", async () => {
    const source = await readFile("src/lib/components/VideoPlayer.svelte", "utf8");

    expect(source).toContain("const video = mediaElement();");
    expect(source).toContain("video.currentTime = target");
    expect(source).toContain("await video.play();");
    expect(source).toContain("mediaElement()?.pause();");
    expect(source).toContain('video.addEventListener("timeupdate", onNativeTimeUpdate)');
    expect(source).toContain('video.addEventListener("progress", onProgress)');
  });

  it("shows the loading spinner on a cold play before the first segment is renderable", async () => {
    const source = await readFile("src/lib/components/VideoPlayer.svelte", "utf8");

    // A cold on-demand remux has no renderable data yet, so 'canplay'/'playing' are delayed and
    // 'waiting' never fires from a standing start. Buffering must be raised on the play intent
    // (both the explicit play path and the 'play' event) so the play button spins instead of
    // flashing to a dead pause icon over a black frame.
    expect(source).toContain("(mediaElement()?.readyState ?? 0) < 3 /* HAVE_FUTURE_DATA */");
    const coldBufferingMatches = source.match(
      /\(mediaElement\(\)\?\.readyState \?\? 0\) < 3 \/\* HAVE_FUTURE_DATA \*\/\) \{\s*buffering = true;/g,
    );
    expect(coldBufferingMatches?.length).toBe(2);
  });

  it("lets in-player marker chips seek without being swallowed by the overlay", async () => {
    const source = await readFile("src/lib/components/VideoPlayer.svelte", "utf8");

    expect(source).toContain('data-testid="video-marker-chip"');
    expect(source).toContain("pointer-events-auto order-3 hidden flex-wrap gap-1.5");
    expect(source).toContain("sm:flex");
    expect(source).toContain("onpointerdown={(event) => event.stopPropagation()}");
    expect(source).toContain("aria-label={`Seek to ${marker.title}`}");
  });

  it("keeps mobile settings top-level and desktop settings inside the video drawer", async () => {
    const source = await readFile("src/lib/components/VideoSettingsMenu.svelte", "utf8");

    expect(source).toContain("z-index: 1005");
    expect(source).toContain("height: min(34dvh, 18rem)");
    expect(source).toContain("max-height: calc(100dvh - 1.5rem)");
    expect(source).toContain("top: auto");
    expect(source).toContain("bottom: 7.25rem");
    expect(source).toContain("height: auto");
    expect(source).toContain("position: absolute");
    expect(source).toContain("animation: player-settings-flyout-in var(--duration-moderate) var(--ease-enter)");
  });

  it("keeps the sidecar button square like the other player controls", async () => {
    const source = await readFile("src/lib/components/VideoPlayer.svelte", "utf8");

    expect(source).toContain(".sidecar-control-button {\n    justify-content: center;\n    padding: 0;\n    width: 1.75rem;");
    expect(source).toContain(".sidecar-control-button {\n      padding: 0;\n      width: 2.25rem;");
  });

  it("uses menu-row subtitle style controls with compact range inputs", async () => {
    const source = await readFile("src/lib/components/VideoSettingsMenu.svelte", "utf8");

    expect(source).toContain("player-settings-control");
    expect(source).toContain("player-settings-separator");
    expect(source).toContain("style={`--range-progress: ${rangeProgress");
    expect(source).toContain('.player-settings-control input[type="range"]');
    expect(source).toContain("border-radius: var(--radius-xs);");
  });

  it("shows a trickplay frame in the seekbar hover preview", async () => {
    vi.mocked(fetch).mockImplementation((input) => {
      const url = String(input);
      if (url.includes("/Trickplay/320/tiles.m3u8")) {
        return Promise.resolve(new Response(
          [
            "#EXTM3U",
            "#EXT-X-IMAGES-ONLY",
            "#EXT-X-TILES:RESOLUTION=320x180,LAYOUT=2x1,DURATION=5",
            "#EXTINF:10,",
            "0.jpg"
          ].join("\n"),
          { headers: { "Content-Type": "application/vnd.apple.mpegurl" } },
        ));
      }

      return Promise.resolve(
        new Response(JSON.stringify({ state: "ready", renditions: [] }), {
          headers: { "Content-Type": "application/json" },
        }),
      );
    });

    render(VideoPlayer, {
      props: {
        src: "/api/videos/video-1/hls/master.m3u8",
        duration: 10,
        defaultPlaybackMode: "hls",
        trickplayPlaylist: "/Videos/video-1/Trickplay/320/tiles.m3u8",
      },
    });

    const track = screen.getByTestId("video-progress-track");
    Object.defineProperty(track, "getBoundingClientRect", {
      configurable: true,
      value: () => ({
        bottom: 10,
        height: 10,
        left: 0,
        right: 200,
        top: 0,
        width: 200,
        x: 0,
        y: 0,
        toJSON: () => ({}),
      }),
    });

    await waitFor(() => {
      expect(fetch).toHaveBeenCalledWith("/Videos/video-1/Trickplay/320/tiles.m3u8");
    });

    await fireEvent.pointerMove(track, { clientX: 150 });

    const preview = await screen.findByTestId("timeline-trickplay-preview");
    expect(preview.getAttribute("style")).toContain("/Videos/video-1/Trickplay/320/0.jpg");
  });

  it("defines hover, focus, and click feedback for player controls", async () => {
    const source = await readFile("src/lib/components/VideoPlayer.svelte", "utf8");

    expect(source).toContain(".player-control-button:hover");
    expect(source).toContain(".player-control-button:focus-visible");
    expect(source).toContain(".player-control-button.is-pressed");
  });

  it("waits for hls2 readiness before attaching the manifest to Vidstack", async () => {
    let resolveStatus!: (response: Response) => void;
    const statusResponse = new Promise<Response>((resolve) => {
      resolveStatus = resolve;
    });
    vi.mocked(fetch).mockReturnValueOnce(statusResponse);

    render(VideoPlayer, {
      props: {
        src: "/api/video-stream/video-1/hls2/master.m3u8",
        defaultPlaybackMode: "hls",
      },
    });

    await waitFor(() => {
      expect(fetch).toHaveBeenCalledWith(
        "/api/video-stream/video-1/hls2/status",
        expect.objectContaining({ cache: "no-store" }),
      );
    });

    expect(document.querySelector("media-player")).toBeNull();

    resolveStatus(
      new Response(JSON.stringify({ state: "ready", renditions: [] }), {
        headers: { "Content-Type": "application/json" },
      }),
    );

    await waitFor(() => {
      expect(document.querySelector("media-player")?.getAttribute("src")).toBe(
        "/api/video-stream/video-1/hls2/master.m3u8",
      );
    });
  });

  it("attaches manifests directly because the .NET API has no readiness endpoint", async () => {
    render(VideoPlayer, {
      props: {
        src: "/api/videos/video-1/hls/master.m3u8",
        defaultPlaybackMode: "hls",
      },
    });

    await waitFor(() => {
      expect(document.querySelector("media-player")?.getAttribute("src")).toBe(
        "/api/videos/video-1/hls/master.m3u8",
      );
    });
    expect(fetch).not.toHaveBeenCalledWith(
      "/api/videos/video-1/hls/status",
      expect.anything(),
    );
  });

  // Phase 1 parity: when the server hands the browser a stream it cannot actually decode
  // (e.g. an optimistic HEVC/DOVI remux), a fatal media error must escalate to a re-negotiated
  // transcode rather than dead-ending — mirroring Jellyfin's re-request-with-DirectPlay-off recovery.
  async function renderWithFatalErrorSource(
    onForceTranscode: ((atSeconds: number) => Promise<string | null>) | undefined,
  ) {
    render(VideoPlayer, {
      props: {
        src: "/api/videos/video-1/hls/master.m3u8",
        directSrc: "",
        defaultPlaybackMode: "hls",
        onForceTranscode,
      },
    });
    const player = await waitFor(() => {
      const el = document.querySelector("media-player");
      expect(el?.getAttribute("src")).toBe("/api/videos/video-1/hls/master.m3u8");
      return el as Element;
    });
    return player;
  }

  it("recovers from a fatal decode error by negotiating a forced transcode and swapping in place", async () => {
    const onForceTranscode = vi
      .fn<(atSeconds: number) => Promise<string | null>>()
      .mockResolvedValue("/api/videos/video-1/hls/forced.m3u8");
    const player = await renderWithFatalErrorSource(onForceTranscode);

    await fireEvent(player, new CustomEvent("error", { detail: new Error("PIPELINE_ERROR_DECODE") }));

    await waitFor(() => expect(onForceTranscode).toHaveBeenCalledTimes(1));
    await waitFor(() => {
      expect(document.querySelector("media-player")?.getAttribute("src")).toBe(
        "/api/videos/video-1/hls/forced.m3u8",
      );
    });
  });

  it("negotiates a forced transcode at most once per source", async () => {
    const onForceTranscode = vi
      .fn<(atSeconds: number) => Promise<string | null>>()
      .mockResolvedValue("/api/videos/video-1/hls/forced.m3u8");
    const player = await renderWithFatalErrorSource(onForceTranscode);

    await fireEvent(player, new CustomEvent("error", { detail: new Error("decode") }));
    await waitFor(() => expect(onForceTranscode).toHaveBeenCalledTimes(1));
    await waitFor(() => {
      expect(document.querySelector("media-player")?.getAttribute("src")).toBe(
        "/api/videos/video-1/hls/forced.m3u8",
      );
    });

    const swapped = document.querySelector("media-player") as Element;
    await fireEvent(swapped, new CustomEvent("error", { detail: new Error("decode again") }));
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(onForceTranscode).toHaveBeenCalledTimes(1);
  });

  it("does NOT force a transcode on a transient/network error (keeps the remux)", async () => {
    // A network/abort/startup error must not tear down a working remux. Only a genuine decode
    // failure escalates; everything else recovers on its own.
    const onForceTranscode = vi
      .fn<(atSeconds: number) => Promise<string | null>>()
      .mockResolvedValue("/api/videos/video-1/hls/forced.m3u8");
    const player = await renderWithFatalErrorSource(onForceTranscode);

    await fireEvent(player, new CustomEvent("error", { detail: { code: 2, message: "network error" } }));
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(onForceTranscode).not.toHaveBeenCalled();
    expect(document.querySelector("media-player")?.getAttribute("src")).toBe(
      "/api/videos/video-1/hls/master.m3u8",
    );
  });

  it("shows a terminal notice when no compatible stream is available", async () => {
    const onForceTranscode = vi
      .fn<(atSeconds: number) => Promise<string | null>>()
      .mockResolvedValue(null);
    const player = await renderWithFatalErrorSource(onForceTranscode);

    await fireEvent(player, new CustomEvent("error", { detail: new Error("decode") }));

    await waitFor(() => expect(onForceTranscode).toHaveBeenCalledTimes(1));
    expect(document.querySelector("media-player")?.getAttribute("src")).toBe(
      "/api/videos/video-1/hls/master.m3u8",
    );
  });
});
