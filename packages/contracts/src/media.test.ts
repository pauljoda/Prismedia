import { describe, it, expect } from "vitest";
import {
  formatDuration,
  formatFileSize,
  getHlsRenditions,
  getResolutionLabel,
  HLS_RENDITION_PRESETS,
  isVideoImageFormat,
  isVideoImage,
  canUseInlineVideoPreview,
  VIDEO_PREVIEW_MAX_FILE_SIZE_BYTES,
} from "./media";

describe("formatDuration", () => {
  it("returns null for null/undefined/zero", () => {
    expect(formatDuration(null)).toBeNull();
    expect(formatDuration(undefined)).toBeNull();
    expect(formatDuration(0)).toBeNull();
  });

  it("formats seconds under an hour", () => {
    expect(formatDuration(65)).toBe("1:05");
    expect(formatDuration(3599)).toBe("59:59");
    expect(formatDuration(5)).toBe("0:05");
  });

  it("formats hours", () => {
    expect(formatDuration(3600)).toBe("1:00:00");
    expect(formatDuration(3661)).toBe("1:01:01");
    expect(formatDuration(7200)).toBe("2:00:00");
  });

  it("floors fractional seconds", () => {
    expect(formatDuration(65.9)).toBe("1:05");
  });
});

describe("formatFileSize", () => {
  it("returns null for null/undefined/zero", () => {
    expect(formatFileSize(null)).toBeNull();
    expect(formatFileSize(undefined)).toBeNull();
    expect(formatFileSize(0)).toBeNull();
  });

  it("formats KB", () => {
    expect(formatFileSize(512 * 1024)).toBe("512 KB");
  });

  it("formats MB", () => {
    expect(formatFileSize(150 * 1024 * 1024)).toBe("150 MB");
  });

  it("formats GB", () => {
    expect(formatFileSize(2.5 * 1024 * 1024 * 1024)).toBe("2.5 GB");
  });
});

describe("getResolutionLabel", () => {
  it("returns null for null/undefined/zero", () => {
    expect(getResolutionLabel(null)).toBeNull();
    expect(getResolutionLabel(undefined)).toBeNull();
    expect(getResolutionLabel(0)).toBeNull();
  });

  it("maps standard resolutions", () => {
    expect(getResolutionLabel(2160)).toBe("4K");
    expect(getResolutionLabel(1080)).toBe("1080p");
    expect(getResolutionLabel(720)).toBe("720p");
    expect(getResolutionLabel(480)).toBe("480p");
  });

  it("handles non-standard heights", () => {
    expect(getResolutionLabel(360)).toBe("360p");
    expect(getResolutionLabel(4320)).toBe("4K");
  });
});

describe("isVideoImageFormat", () => {
  it("recognizes video codec formats", () => {
    expect(isVideoImageFormat("h264")).toBe(true);
    expect(isVideoImageFormat("hevc")).toBe(true);
    expect(isVideoImageFormat("vp9")).toBe(true);
    expect(isVideoImageFormat("av1")).toBe(true);
  });

  it("recognizes container formats", () => {
    expect(isVideoImageFormat("mp4")).toBe(true);
    expect(isVideoImageFormat("mkv")).toBe(true);
    expect(isVideoImageFormat("webm")).toBe(true);
  });

  it("is case-insensitive", () => {
    expect(isVideoImageFormat("H264")).toBe(true);
    expect(isVideoImageFormat("HEVC")).toBe(true);
  });

  it("rejects non-video formats", () => {
    expect(isVideoImageFormat("jpeg")).toBe(false);
    expect(isVideoImageFormat("png")).toBe(false);
    expect(isVideoImageFormat(null)).toBe(false);
    expect(isVideoImageFormat(undefined)).toBe(false);
  });
});

describe("isVideoImage", () => {
  it("detects by isVideo flag", () => {
    expect(isVideoImage({ isVideo: true })).toBe(true);
    expect(isVideoImage({ isVideo: false })).toBe(false);
  });

  it("detects by format", () => {
    expect(isVideoImage({ format: "h264" })).toBe(true);
    expect(isVideoImage({ format: "jpeg" })).toBe(false);
  });

  it("detects by title extension", () => {
    expect(isVideoImage({ title: "clip.mp4" })).toBe(true);
    expect(isVideoImage({ title: "photo.jpg" })).toBe(false);
  });
});

describe("canUseInlineVideoPreview", () => {
  it("requires video + previewPath", () => {
    expect(canUseInlineVideoPreview({ isVideo: true, previewPath: "/p" })).toBe(true);
    expect(canUseInlineVideoPreview({ isVideo: true, previewPath: null })).toBe(false);
    expect(canUseInlineVideoPreview({ isVideo: false, previewPath: "/p" })).toBe(false);
  });

  it("rejects files over 50MB", () => {
    expect(
      canUseInlineVideoPreview({
        isVideo: true,
        previewPath: "/p",
        fileSize: VIDEO_PREVIEW_MAX_FILE_SIZE_BYTES + 1,
      }),
    ).toBe(false);
  });

  it("allows files at or under 50MB", () => {
    expect(
      canUseInlineVideoPreview({
        isVideo: true,
        previewPath: "/p",
        fileSize: VIDEO_PREVIEW_MAX_FILE_SIZE_BYTES,
      }),
    ).toBe(true);
  });

  it("allows null fileSize", () => {
    expect(
      canUseInlineVideoPreview({ isVideo: true, previewPath: "/p", fileSize: null }),
    ).toBe(true);
  });
});

describe("getHlsRenditions", () => {
  it("returns all presets at or below 1080p for a 1080p source", () => {
    const renditions = getHlsRenditions(1080);
    expect(renditions.map((r) => r.name)).toEqual([
      "1080p",
      "720p",
      "480p",
      "360p",
      "240p",
      "180p",
    ]);
  });

  it("drops presets above the source height", () => {
    const renditions = getHlsRenditions(720);
    expect(renditions.map((r) => r.name)).toEqual([
      "720p",
      "480p",
      "360p",
      "240p",
      "180p",
    ]);
    expect(renditions.every((r) => r.height <= 720)).toBe(true);
  });

  it("handles 480p sources", () => {
    const renditions = getHlsRenditions(480);
    expect(renditions.map((r) => r.name)).toEqual(["480p", "360p", "240p", "180p"]);
  });

  it("falls back to a single custom rendition for very small sources", () => {
    const renditions = getHlsRenditions(120);
    expect(renditions).toHaveLength(1);
    expect(renditions[0]).toMatchObject({ name: "120p", height: 120 });
  });

  it("defaults null/undefined sources to 720p filtering", () => {
    expect(getHlsRenditions(null).map((r) => r.name)).toEqual([
      "720p",
      "480p",
      "360p",
      "240p",
      "180p",
    ]);
    expect(getHlsRenditions(undefined).map((r) => r.name)).toEqual([
      "720p",
      "480p",
      "360p",
      "240p",
      "180p",
    ]);
  });

  it("returns cloned objects so callers cannot mutate the preset table", () => {
    const renditions = getHlsRenditions(1080);
    renditions[0].label = "mutated";
    expect(HLS_RENDITION_PRESETS[0].label).toBe("1080p");
  });
});
