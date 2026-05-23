import { describe, expect, it } from "vitest";
import {
  acceptForCategory,
  categoryForTarget,
  uploadTargetLabel,
  type UploadTarget,
} from "./upload-types";

describe("upload target metadata", () => {
  it("maps each scoped upload target to the compatible file category", () => {
    const cases: [UploadTarget, string][] = [
      [{ kind: "video", libraryRootId: "root-1" }, "video"],
      [{ kind: "video", videoSeriesId: "series-1", seasonNumber: 2 }, "video"],
      [{ kind: "image", libraryRootId: "root-1" }, "image"],
      [{ kind: "image", galleryId: "gallery-1" }, "image"],
      [{ kind: "audio", audioLibraryId: "audio-1" }, "audio"],
    ];

    for (const [target, category] of cases) {
      expect(categoryForTarget(target)).toBe(category);
    }
  });

  it("keeps browser accept filters aligned with the server allow-list", () => {
    expect(acceptForCategory("video")).toContain(".mkv");
    expect(acceptForCategory("image")).toContain(".webp");
    expect(acceptForCategory("audio")).toContain(".flac");
  });

  it("describes picker-backed root targets without pretending a folder is known", () => {
    expect(uploadTargetLabel({ kind: "video" })).toBe("video files");
    expect(uploadTargetLabel({ kind: "image" })).toBe("image files");
    expect(uploadTargetLabel({ kind: "audio" })).toBe("audio files");
  });
});
