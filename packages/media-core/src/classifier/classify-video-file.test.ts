import { describe, it, expect } from "vitest";
import { classifyVideoFile } from "./classify-video-file";

const root = {
  libraryRootPath: "/media/library",
};

describe("classifyVideoFile", () => {
  describe("depth 0 — files at library root", () => {
    it("classifies as movie", () => {
      const result = classifyVideoFile("/media/library/Blade Runner (1982).mkv", root);
      expect(result).toEqual({
        kind: "movie",
        filePath: "/media/library/Blade Runner (1982).mkv",
        libraryRootPath: "/media/library",
      });
    });
  });

  describe("depth 1 — files inside a series folder (flat series)", () => {
    it("classifies as episode with seasonFolderPath=null and placementSeasonNumber=0", () => {
      const result = classifyVideoFile(
        "/media/library/The Expanse/S01E01.mkv",
        root,
      );
      expect(result.kind).toBe("episode");
      if (result.kind === "episode") {
        expect(result.seriesFolderPath).toBe("/media/library/The Expanse");
        expect(result.seriesFolderName).toBe("The Expanse");
        expect(result.seasonFolderPath).toBeNull();
        expect(result.seasonFolderName).toBeNull();
        expect(result.placementSeasonNumber).toBe(0);
      }
    });
  });

  describe("depth 2 — files inside a season folder", () => {
    it("classifies with the parsed season number", () => {
      const result = classifyVideoFile(
        "/media/library/Breaking Bad/Season 1/S01E01.mkv",
        root,
      );
      expect(result.kind).toBe("episode");
      if (result.kind === "episode") {
        expect(result.seriesFolderPath).toBe("/media/library/Breaking Bad");
        expect(result.seriesFolderName).toBe("Breaking Bad");
        expect(result.seasonFolderPath).toBe("/media/library/Breaking Bad/Season 1");
        expect(result.seasonFolderName).toBe("Season 1");
        expect(result.placementSeasonNumber).toBe(1);
      }
    });

    it("maps a Specials folder to season 0", () => {
      const result = classifyVideoFile(
        "/media/library/Breaking Bad/Specials/Behind the Scenes.mkv",
        root,
      );
      expect(result.kind).toBe("episode");
      if (result.kind === "episode") {
        expect(result.placementSeasonNumber).toBe(0);
      }
    });

    it("maps an unrecognized depth-2 folder to season 0 (Specials default)", () => {
      const result = classifyVideoFile(
        "/media/library/Breaking Bad/Extras Folder/Interview.mkv",
        root,
      );
      expect(result.kind).toBe("episode");
      if (result.kind === "episode") {
        expect(result.placementSeasonNumber).toBe(0);
      }
    });
  });

  describe("depth >= 3 — rejected", () => {
    it("rejects files nested too deep", () => {
      const result = classifyVideoFile(
        "/media/library/Anime/One Piece/Arc 1/Episode 1.mkv",
        root,
      );
      expect(result.kind).toBe("rejected");
      if (result.kind === "rejected") {
        expect(result.reason).toMatch(/depth/i);
      }
    });
  });

  describe("file not under root", () => {
    it("rejects files that are not under the library root path", () => {
      const result = classifyVideoFile(
        "/elsewhere/random.mkv",
        root,
      );
      expect(result.kind).toBe("rejected");
    });
  });
});
