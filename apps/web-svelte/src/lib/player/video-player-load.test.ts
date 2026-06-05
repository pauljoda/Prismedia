import { describe, expect, it } from "vitest";
import {
  adaptiveAutoLevelSelection,
  adaptiveHlsBufferConfig,
  adaptiveSeekPlan,
  canUseDirectPlayback,
  chooseInitialPlaybackMode,
  computeVideoLoadState,
  fallbackPlaybackModeForError,
  hlsStatusUrlForSrc,
  requestedModeFromQualityMode,
} from "./video-player-load";

describe("video-player-load", () => {
  it("treats every non-direct quality mode as adaptive playback", () => {
    expect(requestedModeFromQualityMode("direct")).toBe("direct");
    expect(requestedModeFromQualityMode("auto")).toBe("hls");
    expect(requestedModeFromQualityMode(0)).toBe("hls");
    expect(requestedModeFromQualityMode("seed:720p")).toBe("hls");
  });

  it("prefers direct playback for a brand-new source when a direct stream exists", () => {
    expect(
      chooseInitialPlaybackMode({
        src: "/api/video-stream/video-1/hls2/master.m3u8",
        directSrc: "/api/video-stream/video-1/source",
        defaultPlaybackMode: "direct",
      }),
    ).toBe("direct");

    const state = computeVideoLoadState({
      src: "/api/video-stream/video-1/hls2/master.m3u8",
      directSrc: "/api/video-stream/video-1/source",
      defaultPlaybackMode: "direct",
      requestedMode: "hls",
      prevSrcKey: "",
    });

    expect(state.isNewSource).toBe(true);
    expect(state.effectiveMode).toBe("direct");
    expect(state.loadKey).toBe(
      "/api/video-stream/video-1/hls2/master.m3u8|/api/video-stream/video-1/source|direct",
    );
  });

  it("requires browser HEVC support before direct-playing HEVC sources", () => {
    const unsupported = canUseDirectPlayback({
      directSrc: "/api/video-stream/video-1/source",
      codec: "HEVC",
      canPlayType: () => "",
    });

    const supported = canUseDirectPlayback({
      directSrc: "/api/video-stream/video-1/source",
      codec: "h265",
      canPlayType: (mime) => (mime.includes("hvc1") ? "probably" : ""),
    });

    expect(unsupported).toBe(false);
    expect(supported).toBe(true);
  });

  it("starts HEVC sources in adaptive mode when direct playback is unsupported", () => {
    const state = computeVideoLoadState({
      src: "/api/video-stream/video-1/hls2/master.m3u8",
      directSrc: "/api/video-stream/video-1/source",
      defaultPlaybackMode: "direct",
      directPlayable: false,
      requestedMode: "direct",
      prevSrcKey: "",
    });

    expect(state.isNewSource).toBe(true);
    expect(state.effectiveMode).toBe("hls");
    expect(state.loadKey).toBe(
      "/api/video-stream/video-1/hls2/master.m3u8|/api/video-stream/video-1/source|hls",
    );
  });

  it("switches the load key when the same source falls back from direct to hls", () => {
    const direct = computeVideoLoadState({
      src: "/api/video-stream/video-1/hls2/master.m3u8",
      directSrc: "/api/video-stream/video-1/source",
      defaultPlaybackMode: "direct",
      requestedMode: "direct",
      prevSrcKey: "",
    });

    const adaptive = computeVideoLoadState({
      src: "/api/video-stream/video-1/hls2/master.m3u8",
      directSrc: "/api/video-stream/video-1/source",
      defaultPlaybackMode: "direct",
      requestedMode: "hls",
      prevSrcKey: direct.srcKey,
    });

    expect(adaptive.isNewSource).toBe(false);
    expect(adaptive.effectiveMode).toBe("hls");
    expect(adaptive.loadKey).not.toBe(direct.loadKey);
  });

  it("falls back from direct playback to adaptive hls on media errors", () => {
    expect(
      fallbackPlaybackModeForError({
        effectiveMode: "direct",
        hlsSrc: "/Videos/video-1/master.m3u8",
        directSrc: "/Videos/video-1/stream",
      }),
    ).toBe("hls");
  });

  it("does not fall back to a direct source that already failed", () => {
    expect(
      fallbackPlaybackModeForError({
        effectiveMode: "hls",
        hlsSrc: "/Videos/video-1/master.m3u8",
        directSrc: "/Videos/video-1/stream",
        directPlayable: true,
        directFailed: true,
      }),
    ).toBeNull();
  });

  it("keeps the same load key while changing adaptive quality levels", () => {
    const auto = computeVideoLoadState({
      src: "/api/video-stream/video-1/hls2/master.m3u8",
      directSrc: "/api/video-stream/video-1/source",
      defaultPlaybackMode: "direct",
      requestedMode: requestedModeFromQualityMode("auto"),
      prevSrcKey: "/api/video-stream/video-1/hls2/master.m3u8|/api/video-stream/video-1/source",
    });

    const seeded = computeVideoLoadState({
      src: "/api/video-stream/video-1/hls2/master.m3u8",
      directSrc: "/api/video-stream/video-1/source",
      defaultPlaybackMode: "direct",
      requestedMode: requestedModeFromQualityMode("seed:720p"),
      prevSrcKey: auto.srcKey,
    });

    expect(seeded.loadKey).toBe(auto.loadKey);
    expect(seeded.effectiveMode).toBe("hls");
  });

  it("leaves automatic adaptive startup to hls.js instead of forcing the top level", () => {
    expect(adaptiveAutoLevelSelection()).toEqual({
      currentLevel: -1,
      startLevel: -1,
      nextAutoLevel: -1,
    });
  });

  it("keeps a modest forward buffer (coupled to the server reuse window) bounded by bytes", () => {
    const config = adaptiveHlsBufferConfig();
    expect(config).toEqual({
      backBufferLength: 60,
      capLevelToPlayerSize: false,
      frontBufferFlushThreshold: Infinity,
      maxBufferLength: 30,
      maxMaxBufferLength: 30,
      maxBufferSize: 800_000_000,
      startLevel: -1,
      startPosition: 0,
      fragLoadPolicy: {
        default: {
          maxTimeToFirstByteMs: 60_000,
          maxLoadTimeMs: 120_000,
          timeoutRetry: { maxNumRetry: 2, retryDelayMs: 0, maxRetryDelayMs: 0 },
          errorRetry: { maxNumRetry: 4, retryDelayMs: 1000, maxRetryDelayMs: 8000 },
        },
      },
      manifestLoadPolicy: {
        default: {
          maxTimeToFirstByteMs: 60_000,
          maxLoadTimeMs: 120_000,
          timeoutRetry: { maxNumRetry: 2, retryDelayMs: 0, maxRetryDelayMs: 0 },
          errorRetry: { maxNumRetry: 4, retryDelayMs: 1000, maxRetryDelayMs: 8000 },
        },
      },
      playlistLoadPolicy: {
        default: {
          maxTimeToFirstByteMs: 60_000,
          maxLoadTimeMs: 120_000,
          timeoutRetry: { maxNumRetry: 2, retryDelayMs: 0, maxRetryDelayMs: 0 },
          errorRetry: { maxNumRetry: 4, retryDelayMs: 1000, maxRetryDelayMs: 8000 },
        },
      },
    });
    // Memory is bounded by the byte cap rather than the time length, so a high-bitrate 4K stream
    // cannot grow the buffer without limit.
    expect(config.maxBufferSize).toBeLessThanOrEqual(1_000_000_000);
    // On-demand transcoding can make the first segment slow; the fragment timeout must exceed
    // hls.js's 10s default so playback waits instead of aborting.
    expect(config.fragLoadPolicy.default.maxTimeToFirstByteMs).toBeGreaterThan(10_000);
    // The remux source is a bare media playlist; the manifest/playlist timeouts must also exceed the
    // 10s default so the cold first play (waiting on ffmpeg's first event playlist) doesn't abort.
    expect(config.manifestLoadPolicy.default.maxTimeToFirstByteMs).toBeGreaterThan(10_000);
    expect(config.playlistLoadPolicy.default.maxTimeToFirstByteMs).toBeGreaterThan(10_000);
  });

  it("uses the hls2 readiness endpoint before loading adaptive streams", () => {
    expect(hlsStatusUrlForSrc("/api/video-stream/video-1/hls2/master.m3u8")).toBe(
      "/api/video-stream/video-1/hls2/status",
    );
    expect(
      hlsStatusUrlForSrc("/api/video-stream/video-1/hls2/master.m3u8?token=abc"),
    ).toBe("/api/video-stream/video-1/hls2/status?token=abc");
    expect(hlsStatusUrlForSrc("/api/video-stream/video-1/source")).toBeNull();
    expect(hlsStatusUrlForSrc("/api/videos/video-1/hls/master.m3u8")).toBeNull();
  });

  it("asks hls.js to load from an out-of-buffer adaptive seek instead of clamping", () => {
    expect(
      adaptiveSeekPlan({
        streamMode: "hls",
        target: 960,
        seekableEnd: 165,
        hasManagedHls: true,
      }),
    ).toEqual({
      currentTime: 960,
      deferredSeekTarget: null,
      hlsStartLoadAt: 960,
    });
  });

  it("keeps the old wait-at-edge behavior only when hls.js is unavailable", () => {
    expect(
      adaptiveSeekPlan({
        streamMode: "hls",
        target: 960,
        seekableEnd: 165,
        hasManagedHls: false,
      }),
    ).toEqual({
      currentTime: 164.5,
      deferredSeekTarget: 960,
      hlsStartLoadAt: null,
    });
  });
});
