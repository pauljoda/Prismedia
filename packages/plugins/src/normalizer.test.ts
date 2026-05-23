import { describe, expect, it } from "vitest";
import { normalizeBookResult, normalizeGalleryResult } from "./normalizer";

describe("normalizeGalleryResult", () => {
  it("preserves gallery candidate metadata for disambiguation", () => {
    const result = normalizeGalleryResult({
      title: "Berserk",
      externalIds: { mangadex: "manga-1", language: "en" },
      isNsfw: true,
      candidates: [
        {
          externalIds: { mangadex: "manga-1", language: "en" },
          title: "Berserk",
          year: 1989,
          overview: "English description",
          posterUrl: "https://uploads.mangadex.org/covers/manga-1/cover.jpg",
          language: "en",
          contentRating: "safe",
          source: "mangadex",
        },
        {
          externalIds: { mangadex: "manga-1", language: "ja" },
          title: "ベルセルク",
          year: 1989,
          overview: "Japanese title",
          posterUrl: "not-a-url",
          language: "ja",
          contentRating: "safe",
          source: "mangadex",
        },
      ],
    });

    expect(result.externalIds).toEqual({ mangadex: "manga-1", language: "en" });
    expect(result.isNsfw).toBe(true);
    expect(result.candidates).toEqual([
      {
        externalIds: { mangadex: "manga-1", language: "en" },
        title: "Berserk",
        year: 1989,
        overview: "English description",
        posterUrl: "https://uploads.mangadex.org/covers/manga-1/cover.jpg",
        language: "en",
        contentRating: "safe",
        source: "mangadex",
      },
      {
        externalIds: { mangadex: "manga-1", language: "ja" },
        title: "ベルセルク",
        year: 1989,
        overview: "Japanese title",
        posterUrl: null,
        language: "ja",
        contentRating: "safe",
        source: "mangadex",
      },
    ]);
  });
});

describe("normalizeBookResult", () => {
  it("preserves book candidate metadata for language-aware picking", () => {
    const result = normalizeBookResult({
      title: "She Was Cute Before",
      externalIds: { mangadex: "manga-1", language: "en" },
      isNsfw: true,
      candidates: [
        {
          externalIds: { mangadex: "manga-1", language: "en" },
          title: "She Was Cute Before",
          year: 2024,
          overview: "English description",
          posterUrl: "https://uploads.mangadex.org/covers/manga-1/cover.jpg",
          language: "en",
          contentRating: "pornographic",
          source: "mangadex",
        },
      ],
    });

    expect(result.externalIds).toEqual({ mangadex: "manga-1", language: "en" });
    expect(result.isNsfw).toBe(true);
    expect(result.candidates).toEqual([
      {
        externalIds: { mangadex: "manga-1", language: "en" },
        title: "She Was Cute Before",
        year: 2024,
        overview: "English description",
        posterUrl: "https://uploads.mangadex.org/covers/manga-1/cover.jpg",
        language: "en",
        contentRating: "pornographic",
        source: "mangadex",
      },
    ]);
  });
});
