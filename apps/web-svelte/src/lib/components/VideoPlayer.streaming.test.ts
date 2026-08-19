import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import VideoPlayer from "./VideoPlayer.svelte";

vi.mock("vidstack/player", () => ({}));
vi.mock("vidstack/player/layouts", () => ({}));
vi.mock("vidstack/player/ui", () => ({}));
vi.mock("vidstack", () => ({
  isHLSProvider: () => false,
}));

beforeEach(() => {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ state: "ready", renditions: [] }), {
        headers: { "Content-Type": "application/json" },
      }),
    ),
  );
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
  vi.unstubAllGlobals();
});

describe("VideoPlayer streaming recovery", () => {
  it("pre-warms only direct media metadata", async () => {
    const direct = render(VideoPlayer, {
      props: {
        directSrc: "/api/playback/videos/video-1/stream",
        defaultPlaybackMode: "direct",
      },
    });

    await waitFor(() => {
      const player = document.querySelector("media-player");
      expect(player).toHaveAttribute("load", "eager");
      expect(player).toHaveAttribute("preload", "metadata");
    });
    direct.unmount();

    render(VideoPlayer, {
      props: {
        src: "/api/playback/videos/video-1/hls/v/master.m3u8",
        defaultPlaybackMode: "hls",
      },
    });

    await waitFor(() => {
      expect(document.querySelector("media-player")).toHaveAttribute("load", "play");
    });
  });

  it("aborts a direct metadata request when the player unmounts", async () => {
    const { unmount } = render(VideoPlayer, {
      props: {
        directSrc: "/api/playback/videos/video-1/stream",
        defaultPlaybackMode: "direct",
      },
    });
    const player = await waitFor(() => {
      const element = document.querySelector("media-player");
      expect(element).toBeInTheDocument();
      return element!;
    });
    const video = document.createElement("video");
    video.src = "/api/playback/videos/video-1/stream";
    const pause = vi.spyOn(video, "pause").mockImplementation(() => undefined);
    const load = vi.spyOn(video, "load").mockImplementation(() => undefined);
    player.append(video);

    unmount();

    expect(pause).toHaveBeenCalledOnce();
    expect(load).toHaveBeenCalledOnce();
    expect(video).not.toHaveAttribute("src");
  });

  it("shows the loading status on a cold play before the first segment is renderable", async () => {
    render(VideoPlayer, {
      props: {
        directSrc: "/api/playback/videos/video-1/stream",
        defaultPlaybackMode: "direct",
      },
    });

    const player = await waitFor(() => {
      const element = document.querySelector("media-player");
      expect(element).toBeInTheDocument();
      return element!;
    });

    player.dispatchEvent(new CustomEvent("can-play", { detail: { duration: 100 } }));
    await waitFor(() => {
      expect(screen.queryAllByRole("status", { name: "Loading video" })).toHaveLength(0);
    });

    player.dispatchEvent(new Event("play"));
    await waitFor(() => {
      expect(screen.getAllByRole("status", { name: "Loading video" })).not.toHaveLength(0);
    });
  });

  it("shows a trickplay frame in the seekbar hover preview", async () => {
    vi.mocked(fetch).mockImplementation((input) => {
      const url = String(input);
      if (url.includes("/trickplay/320/tiles.m3u8")) {
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
        trickplayPlaylist: "/api/playback/videos/video-1/trickplay/320/tiles.m3u8",
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
      expect(fetch).toHaveBeenCalledWith("/api/playback/videos/video-1/trickplay/320/tiles.m3u8");
    });

    await fireEvent.pointerMove(track, { clientX: 150 });

    const preview = await screen.findByTestId("timeline-trickplay-preview");
    expect(preview.getAttribute("style")).toContain("/api/playback/videos/video-1/trickplay/320/0.jpg");
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
  // transcode rather than dead-ending after direct and adaptive playback fail.
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
