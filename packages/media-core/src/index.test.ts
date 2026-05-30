import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { describe, it, expect } from "vitest";
import {
  isVideoFile,
  isImageFile,
  fileNameToTitle,
  naturalComparePaths,
  sortPathsNaturally,
  getSidecarPaths,
  getVideoGeneratedDiskPaths,
  allVideoGeneratedDiskPaths,
  videoGeneratedLayoutFromDedicated,
  isAnimatedFormat,
  computeMd5,
  computeMd5AndOsHash,
  computeOsHash,
  computePhash,
  runProcess,
  isCorruptMediaError,
  CorruptMediaError,
  resolveExistingMediaPath,
  getGeneratedCollectionDir,
} from "./index";

async function hasBinary(name: string): Promise<boolean> {
  try {
    await runProcess("which", [name]);
    return true;
  } catch {
    return false;
  }
}

describe("isVideoFile", () => {
  it("recognizes standard video extensions", () => {
    expect(isVideoFile("/media/scene.mp4")).toBe(true);
    expect(isVideoFile("/media/scene.mkv")).toBe(true);
    expect(isVideoFile("/media/scene.avi")).toBe(true);
    expect(isVideoFile("/media/scene.mov")).toBe(true);
    expect(isVideoFile("/media/scene.webm")).toBe(true);
  });

  it("rejects non-video files", () => {
    expect(isVideoFile("/media/photo.jpg")).toBe(false);
    expect(isVideoFile("/media/doc.txt")).toBe(false);
    expect(isVideoFile("/media/audio.mp3")).toBe(false);
  });

  it("skips generated preview/thumbnail files", () => {
    expect(isVideoFile("/media/scene-preview.mp4")).toBe(false);
    expect(isVideoFile("/media/scene_thumb.mp4")).toBe(false);
    expect(isVideoFile("/media/scene.sprite.mp4")).toBe(false);
    expect(isVideoFile("/media/scene-sample.mkv")).toBe(false);
  });

  it("allows files with preview-like words in the middle", () => {
    expect(isVideoFile("/media/my-preview-video-full.mp4")).toBe(true);
  });
});

describe("isImageFile", () => {
  it("recognizes standard image extensions", () => {
    expect(isImageFile("photo.jpg")).toBe(true);
    expect(isImageFile("photo.jpeg")).toBe(true);
    expect(isImageFile("photo.png")).toBe(true);
    expect(isImageFile("photo.webp")).toBe(true);
    expect(isImageFile("photo.gif")).toBe(true);
  });

  it("also recognizes animated/video gallery formats", () => {
    // isImageFile checks supportedGalleryMediaExtensions which includes animated
    expect(isImageFile("video.mp4")).toBe(true);
    expect(isImageFile("video.webm")).toBe(true);
  });

  it("rejects unsupported formats", () => {
    expect(isImageFile("doc.txt")).toBe(false);
    expect(isImageFile("audio.mp3")).toBe(false);
  });
});

describe("natural path sorting", () => {
  it("orders numbered comic pages by numeric filename segments", () => {
    expect(
      sortPathsNaturally([
        "/comic/10.png",
        "/comic/2.png",
        "/comic/1.png",
        "/comic/9.png",
      ]),
    ).toEqual([
      "/comic/1.png",
      "/comic/2.png",
      "/comic/9.png",
      "/comic/10.png",
    ]);
  });

  it("keeps natural compare stable for prefixed page numbers", () => {
    const paths = ["page 010.jpg", "page 002.jpg", "page 001.jpg"];
    expect([...paths].sort(naturalComparePaths)).toEqual([
      "page 001.jpg",
      "page 002.jpg",
      "page 010.jpg",
    ]);
  });
});

describe("fileNameToTitle", () => {
  it("strips extension and replaces separators", () => {
    expect(fileNameToTitle("/media/my-scene_title.mp4")).toBe("my scene title");
  });

  it("decodes HTML entities", () => {
    expect(fileNameToTitle("/media/tom &amp; jerry.mp4")).toBe("tom & jerry");
  });

  it("collapses whitespace", () => {
    expect(fileNameToTitle("/media/a___b---c...d.mp4")).toBe("a b c d");
  });

  it("trims leading/trailing whitespace", () => {
    expect(fileNameToTitle("/media/  scene  .mkv")).toBe("scene");
  });
});

describe("getSidecarPaths", () => {
  it("generates expected sidecar paths", () => {
    const paths = getSidecarPaths("/media/scene.mp4");
    expect(paths.thumbnail).toBe("/media/scene-thumb.jpg");
    expect(paths.cardThumbnail).toBe("/media/scene-card.jpg");
    expect(paths.preview).toBe("/media/scene-preview.mp4");
    expect(paths.sprite).toBe("/media/scene-sprite.jpg");
    expect(paths.trickplayVtt).toBe("/media/scene-trickplay.vtt");
  });
});

describe("getVideoGeneratedDiskPaths", () => {
  const videoId = "550e8400-e29b-41d4-a716-446655440000";
  const videoPath = "/media/videos/video.mp4";

  it("sidecar layout matches getSidecarPaths stems", () => {
    const p = getVideoGeneratedDiskPaths(videoId, videoPath, "sidecar");
    expect(p.thumb).toBe("/media/videos/video-thumb.jpg");
    expect(p.card).toBe("/media/videos/video-card.jpg");
    expect(p.preview).toBe("/media/videos/video-preview.mp4");
    expect(p.sprite).toBe("/media/videos/video-sprite.jpg");
    expect(p.trickplay).toBe("/media/videos/video-trickplay.vtt");
  });

  it("dedicated layout uses cache root videos/<id>/ and fixed filenames", () => {
    const prev = process.env.PRISMEDIA_CACHE_DIR;
    process.env.PRISMEDIA_CACHE_DIR = "/data/cache";
    try {
      const p = getVideoGeneratedDiskPaths(videoId, videoPath, "dedicated");
      const base = `/data/cache/videos/${videoId}`;
      expect(p.thumb).toBe(`${base}/thumbnail.jpg`);
      expect(p.card).toBe(`${base}/card.jpg`);
      expect(p.preview).toBe(`${base}/preview.mp4`);
      expect(p.sprite).toBe(`${base}/sprite.jpg`);
      expect(p.trickplay).toBe(`${base}/trickplay.vtt`);
    } finally {
      if (prev === undefined) delete process.env.PRISMEDIA_CACHE_DIR;
      else process.env.PRISMEDIA_CACHE_DIR = prev;
    }
  });

  it("allVideoGeneratedDiskPaths dedupes across layouts", () => {
    const prev = process.env.PRISMEDIA_CACHE_DIR;
    process.env.PRISMEDIA_CACHE_DIR = "/c";
    try {
      const all = allVideoGeneratedDiskPaths(videoId, videoPath);
      expect(all.length).toBe(10);
      expect(new Set(all).size).toBe(10);
    } finally {
      if (prev === undefined) delete process.env.PRISMEDIA_CACHE_DIR;
      else process.env.PRISMEDIA_CACHE_DIR = prev;
    }
  });

  it("videoGeneratedLayoutFromDedicated maps booleans", () => {
    expect(videoGeneratedLayoutFromDedicated(true)).toBe("dedicated");
    expect(videoGeneratedLayoutFromDedicated(false)).toBe("sidecar");
  });
});

describe("getGeneratedCollectionDir", () => {
  it("stores collection assets under the cache collections directory", () => {
    const prev = process.env.PRISMEDIA_CACHE_DIR;
    process.env.PRISMEDIA_CACHE_DIR = "/data/cache";
    try {
      expect(getGeneratedCollectionDir("collection-1")).toBe(
        path.join("/data/cache", "collections", "collection-1"),
      );
    } finally {
      if (prev === undefined) delete process.env.PRISMEDIA_CACHE_DIR;
      else process.env.PRISMEDIA_CACHE_DIR = prev;
    }
  });
});

describe("computePhash", () => {
  it("returns null when duration is zero or negative", async () => {
    // No binary call happens — duration guard short-circuits.
    expect(await computePhash("/nonexistent.mp4", 0)).toBeNull();
    expect(await computePhash("/nonexistent.mp4", -5)).toBeNull();
    expect(await computePhash("/nonexistent.mp4", null)).toBeNull();
    expect(await computePhash("/nonexistent.mp4", undefined)).toBeNull();
  });

  it("returns null with a warning when the helper binary is missing", async () => {
    const prev = process.env.PRISMEDIA_PHASH_BIN;
    process.env.PRISMEDIA_PHASH_BIN = "/nonexistent/prismedia-phash-missing";
    try {
      const result = await computePhash("/nonexistent.mp4", 10);
      expect(result).toBeNull();
    } finally {
      if (prev === undefined) delete process.env.PRISMEDIA_PHASH_BIN;
      else process.env.PRISMEDIA_PHASH_BIN = prev;
    }
  });

  it("produces a 16-char hex hash for a real video and is deterministic", { timeout: 30_000 }, async () => {
    const ffmpegAvailable = await hasBinary("ffmpeg");
    const phashAvailable = await hasBinary("prismedia-phash");
    if (!ffmpegAvailable || !phashAvailable) {
      // Skip cleanly when the toolchain isn't installed on this dev machine.
      return;
    }

    const dir = mkdtempSync(path.join(tmpdir(), "prismedia-phash-test-"));
    const video = path.join(dir, "test.mp4");
    try {
      await runProcess("ffmpeg", [
        "-y",
        "-f", "lavfi",
        "-i", "testsrc=duration=5:size=320x180:rate=30",
        "-c:v", "libx264",
        "-pix_fmt", "yuv420p",
        video,
      ]);

      const first = await computePhash(video, 5);
      const second = await computePhash(video, 5);
      expect(first).not.toBeNull();
      expect(first).toMatch(/^[0-9a-f]{16}$/);
      expect(first).toBe(second);
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });
});

describe("isCorruptMediaError", () => {
  it("matches ffprobe errors for structurally broken MP4 containers", () => {
    const err = new Error(
      "ffprobe exited with code 1: [mov,mp4,m4a,3gp,3g2,mj2 @ 0x7abe30452600] moov atom not found\n" +
        "/media/broken.mp4: Invalid data found when processing input",
    );
    expect(isCorruptMediaError(err)).toBe(true);
  });

  it("matches ffprobe 'could not find codec parameters'", () => {
    const err = new Error(
      "ffprobe exited with code 1: Could not find codec parameters for stream 0",
    );
    expect(isCorruptMediaError(err)).toBe(true);
  });

  it("rejects unrelated errors", () => {
    expect(isCorruptMediaError(new Error("ENOENT: no such file"))).toBe(false);
    expect(isCorruptMediaError("not even an Error")).toBe(false);
    expect(isCorruptMediaError(null)).toBe(false);
  });

  it("CorruptMediaError carries the offending path", () => {
    const err = new CorruptMediaError("/media/broken.mp4", new Error("moov atom not found"));
    expect(err.filePath).toBe("/media/broken.mp4");
    expect(err.message).toContain("/media/broken.mp4");
    expect(err.message).toContain("moov atom not found");
    expect(err instanceof Error).toBe(true);
  });
});

describe("isAnimatedFormat", () => {
  it("recognizes animated/video formats by file path", () => {
    expect(isAnimatedFormat("clip.gif")).toBe(true);
    expect(isAnimatedFormat("clip.webm")).toBe(true);
    expect(isAnimatedFormat("clip.mp4")).toBe(true);
  });

  it("rejects static image formats", () => {
    expect(isAnimatedFormat("photo.jpg")).toBe(false);
    expect(isAnimatedFormat("photo.png")).toBe(false);
    expect(isAnimatedFormat("photo.tiff")).toBe(false);
  });
});

describe("resolveExistingMediaPath", () => {
  it("returns the original path when the file already exists", () => {
    const tempDir = mkdtempSync(path.join(tmpdir(), "prismedia-media-path-"));
    const existingPath = path.join(tempDir, "synthetic.mp4");
    writeFileSync(existingPath, "");

    try {
      expect(resolveExistingMediaPath(existingPath)).toBe(existingPath);
    } finally {
      rmSync(tempDir, { recursive: true, force: true });
    }
  });
});

describe("computeMd5AndOsHash", () => {
  // Each entry must produce identical hashes whether computed via
  // computeMd5+computeOsHash or via the single-pass combined helper.
  // Sizes span: smaller than the 64 KB head chunk (where head+tail overlap),
  // exactly the chunk size, just above it, and a multi-MB size that crosses
  // the 4 MB read buffer boundary so the head capture spans multiple chunks.
  const sizes = [
    16 * 1024,
    64 * 1024,
    96 * 1024,
    5 * 1024 * 1024,
  ];

  for (const size of sizes) {
    it(`matches computeMd5 + computeOsHash on a ${size}-byte file`, async () => {
      const tempDir = mkdtempSync(path.join(tmpdir(), "prismedia-hash-"));
      const file = path.join(tempDir, `payload-${size}.bin`);
      // Deterministic non-zero pattern so byte ordering matters.
      const buf = Buffer.alloc(size);
      for (let i = 0; i < size; i++) {
        buf[i] = (i * 1103515245 + 12345) & 0xff;
      }
      writeFileSync(file, buf);

      try {
        const [md5, oshash, combined] = await Promise.all([
          computeMd5(file),
          computeOsHash(file),
          computeMd5AndOsHash(file),
        ]);
        expect(combined.md5).toBe(md5);
        expect(combined.oshash).toBe(oshash);
      } finally {
        rmSync(tempDir, { recursive: true, force: true });
      }
    });
  }
});
