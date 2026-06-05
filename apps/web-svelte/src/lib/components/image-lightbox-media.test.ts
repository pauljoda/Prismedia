import { describe, expect, it } from "vitest";
import {
  buildLightboxImageSource,
  buildLightboxVideoSources,
  mimeTypeForImageVideoFormat,
} from "./image-lightbox-media";

const baseImage = {
  id: "image-1",
  title: "clip.mp4",
  format: "mp4",
  isVideo: true,
  previewPath: "/assets/images/image-1/preview",
  fullPath: "/assets/images/image-1/full",
  thumbnailPath: "/assets/images/image-1/thumb",
};

describe("image-lightbox-media", () => {
  it("uses the generated preview before the original video for playback", () => {
    expect(buildLightboxVideoSources(baseImage)).toEqual([
      { src: "/assets/images/image-1/preview", type: "video/mp4", quality: "fallback" },
      { src: "/assets/images/image-1/full", type: "video/mp4", quality: "original" },
    ]);
  });

  it("prefers the MP4 preview when the original is WebM", () => {
    expect(buildLightboxVideoSources({ ...baseImage, format: "webm", title: "clip.webm" })).toEqual([
      { src: "/assets/images/image-1/preview", type: "video/mp4", quality: "fallback" },
      { src: "/assets/images/image-1/full", type: "video/webm", quality: "original" },
    ]);
  });

  it("does not duplicate the same URL when only one video path is available", () => {
    expect(buildLightboxVideoSources({ ...baseImage, previewPath: "/assets/images/image-1/full" })).toEqual([
      { src: "/assets/images/image-1/full", type: "video/mp4", quality: "fallback" },
    ]);
  });

  it("uses full-size still images before previews and thumbnails", () => {
    expect(buildLightboxImageSource(baseImage)).toBe("/assets/images/image-1/full");
    expect(buildLightboxImageSource({ ...baseImage, fullPath: null })).toBe("/assets/images/image-1/preview");
    expect(buildLightboxImageSource({ ...baseImage, fullPath: null, previewPath: null })).toBe(
      "/assets/images/image-1/thumb",
    );
  });

  it("maps common animated/video image formats to browser MIME types", () => {
    expect(mimeTypeForImageVideoFormat("mp4", "clip.bin")).toBe("video/mp4");
    expect(mimeTypeForImageVideoFormat("h264", "clip.bin")).toBe("video/mp4");
    expect(mimeTypeForImageVideoFormat("webm", "clip.bin")).toBe("video/webm");
    expect(mimeTypeForImageVideoFormat("matroska", "clip.bin")).toBe("video/x-matroska");
    expect(mimeTypeForImageVideoFormat(null, "clip.mov")).toBe("video/quicktime");
    expect(mimeTypeForImageVideoFormat(null, "clip.unknown")).toBeUndefined();
  });
});
