import { describe, expect, it } from "vitest";
import {
  audioFormatBadge,
  channelLayoutLabel,
  dynamicRangeBadge,
  playbackMethodBadge,
  resolutionBadge,
  videoCodecBadge,
} from "./media-badges";

describe("resolutionBadge", () => {
  it("maps common 16:9 dimensions to marketing tiers", () => {
    expect(resolutionBadge(3840, 2160)).toBe("4K");
    expect(resolutionBadge(2560, 1440)).toBe("1440p");
    expect(resolutionBadge(1920, 1080)).toBe("1080p");
    expect(resolutionBadge(1280, 720)).toBe("720p");
    expect(resolutionBadge(720, 480)).toBe("480p");
  });

  it("reads 4K from width when the frame is letterboxed (scope)", () => {
    expect(resolutionBadge(3840, 1600)).toBe("4K");
    expect(resolutionBadge(1920, 800)).toBe("1080p");
  });

  it("reads HD from height for 4:3 sources where width is small", () => {
    expect(resolutionBadge(1440, 1080)).toBe("1080p");
  });

  it("returns null when both dimensions are unknown", () => {
    expect(resolutionBadge(null, undefined)).toBeNull();
    expect(resolutionBadge(0, 0)).toBeNull();
  });
});

describe("dynamicRangeBadge", () => {
  it("labels Dolby Vision regardless of the base-layer compatibility", () => {
    expect(dynamicRangeBadge({ VideoRangeType: "DOVI", DvProfile: 5 })).toBe("Dolby Vision");
    expect(dynamicRangeBadge({ VideoRangeType: "DOVIWithHDR10" })).toBe("Dolby Vision");
    expect(dynamicRangeBadge({ DvProfile: 8 })).toBe("Dolby Vision");
  });

  it("labels the HDR variants", () => {
    expect(dynamicRangeBadge({ VideoRangeType: "HDR10Plus" })).toBe("HDR10+");
    expect(dynamicRangeBadge({ VideoRangeType: "HDR10" })).toBe("HDR10");
    expect(dynamicRangeBadge({ VideoRangeType: "HLG" })).toBe("HLG");
    expect(dynamicRangeBadge({ Hdr10PlusPresentFlag: true, VideoRangeType: "HDR10" })).toBe("HDR10+");
  });

  it("returns null for SDR so it earns no badge", () => {
    expect(dynamicRangeBadge({ VideoRangeType: "SDR" })).toBeNull();
    expect(dynamicRangeBadge({})).toBeNull();
    expect(dynamicRangeBadge(null)).toBeNull();
  });
});

describe("videoCodecBadge", () => {
  it("normalizes common codec ids", () => {
    expect(videoCodecBadge("hevc")).toBe("HEVC");
    expect(videoCodecBadge("h264")).toBe("H.264");
    expect(videoCodecBadge("av1")).toBe("AV1");
    expect(videoCodecBadge("mpeg2video")).toBe("MPEG-2");
  });

  it("upper-cases unknown codecs and ignores blanks", () => {
    expect(videoCodecBadge("theora")).toBe("THEORA");
    expect(videoCodecBadge(null)).toBeNull();
    expect(videoCodecBadge("  ")).toBeNull();
  });
});

describe("channelLayoutLabel", () => {
  it("maps channel counts to layouts", () => {
    expect(channelLayoutLabel(1)).toBe("Mono");
    expect(channelLayoutLabel(2)).toBe("Stereo");
    expect(channelLayoutLabel(6)).toBe("5.1");
    expect(channelLayoutLabel(8)).toBe("7.1");
    expect(channelLayoutLabel(9)).toBe("9 ch");
    expect(channelLayoutLabel(null)).toBeNull();
  });
});

describe("audioFormatBadge", () => {
  it("surfaces object-based formats from the display title with the bed layout", () => {
    expect(
      audioFormatBadge({ Codec: "truehd", Channels: 8, DisplayTitle: "TrueHD 7.1 Atmos" }),
    ).toBe("Dolby Atmos 7.1");
    expect(
      audioFormatBadge({ Codec: "dts", Channels: 8, DisplayTitle: "DTS-HD MA 7.1" }),
    ).toBe("DTS-HD MA 7.1");
  });

  it("names codecs viewers recognize with the channel layout", () => {
    expect(audioFormatBadge({ Codec: "eac3", Channels: 6 })).toBe("Dolby Digital+ 5.1");
    expect(audioFormatBadge({ Codec: "aac", Channels: 2 })).toBe("AAC Stereo");
    expect(audioFormatBadge({ Codec: "flac", Channels: 6 })).toBe("FLAC 5.1");
  });

  it("returns null when there is nothing to describe", () => {
    expect(audioFormatBadge({})).toBeNull();
    expect(audioFormatBadge(null)).toBeNull();
  });
});

describe("playbackMethodBadge", () => {
  it("uses the same terminology as other media servers", () => {
    expect(playbackMethodBadge("direct").label).toBe("Direct Play");
    expect(playbackMethodBadge("remux").label).toBe("Direct Stream");
    expect(playbackMethodBadge("transcode").label).toBe("Transcoding");
  });
});
