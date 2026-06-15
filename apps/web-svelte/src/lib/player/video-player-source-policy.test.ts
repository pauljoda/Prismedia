import { describe, expect, it } from "vitest";
import type { PlayerQualityRung } from "$lib/components/video-player-types";
import { resolveInitialVideoPlayerSourcePolicy } from "./video-player-source-policy";

const rungs: PlayerQualityRung[] = [
  { name: "1080p", label: "1080p - 8 Mbps", bitrate: 8_000_000, url: "/1080p.m3u8" },
  { name: "720p", label: "720p - 4 Mbps", bitrate: 4_000_000, url: "/720p.m3u8" },
];

describe("video-player-source-policy", () => {
  it("starts with direct playback when direct is available and nothing is pinned", () => {
    expect(resolveInitialVideoPlayerSourcePolicy({
      src: "/adaptive.m3u8",
      directSrc: "/source.mp4",
      directAvailable: true,
      savedQuality: "auto",
      qualityRungs: rungs,
    })).toEqual({
      playbackMode: "direct",
      qualityMode: "direct",
      selectedRungName: null,
    });
  });

  it("honors a saved direct preference only when direct is available", () => {
    expect(resolveInitialVideoPlayerSourcePolicy({
      src: "/adaptive.m3u8",
      directSrc: "/source.mp4",
      directAvailable: false,
      savedQuality: "direct",
      qualityRungs: rungs,
    })).toEqual({
      playbackMode: "hls",
      qualityMode: "auto",
      selectedRungName: null,
    });
  });

  it("pins the nearest adaptive rung for a saved bitrate cap", () => {
    expect(resolveInitialVideoPlayerSourcePolicy({
      src: "/adaptive.m3u8",
      directSrc: "/source.mp4",
      directAvailable: true,
      savedQuality: 5_000_000,
      qualityRungs: rungs,
    })).toEqual({
      playbackMode: "hls",
      qualityMode: "720p",
      selectedRungName: "720p",
    });
  });

  it("lets an explicit adaptive default override initial direct preference", () => {
    expect(resolveInitialVideoPlayerSourcePolicy({
      src: "/adaptive.m3u8",
      directSrc: "/source.mp4",
      defaultPlaybackMode: "hls",
      directAvailable: true,
      savedQuality: "auto",
      qualityRungs: rungs,
    })).toEqual({
      playbackMode: "hls",
      qualityMode: "auto",
      selectedRungName: null,
    });
  });
});
