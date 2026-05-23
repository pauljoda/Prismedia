import { describe, it, expect } from "vitest";
import {
  normalizeImageCandidate,
  normalizeMovieResult,
  normalizeSeriesResult,
  normalizeSeasonResult,
  normalizeEpisodeResult,
} from "./normalized-video";

describe("normalizeImageCandidate", () => {
  it("returns null for non-object input", () => {
    expect(normalizeImageCandidate(null)).toBeNull();
    expect(normalizeImageCandidate("https://x.com/img.jpg")).toBeNull();
  });

  it("requires a valid URL", () => {
    expect(normalizeImageCandidate({ url: "" })).toBeNull();
    expect(normalizeImageCandidate({ url: "not-a-url" })).toBeNull();
  });

  it("extracts the full candidate shape", () => {
    const result = normalizeImageCandidate({
      url: "https://image.tmdb.org/t/p/original/abc.jpg",
      language: "en",
      width: 1920,
      height: 1080,
      aspectRatio: 1.78,
      rank: 8.5,
      source: "tmdb",
    });
    expect(result).toEqual({
      url: "https://image.tmdb.org/t/p/original/abc.jpg",
      language: "en",
      width: 1920,
      height: 1080,
      aspectRatio: 1.78,
      rank: 8.5,
      source: "tmdb",
    });
  });

  it("defaults source to 'unknown' if missing", () => {
    const result = normalizeImageCandidate({
      url: "https://image.tmdb.org/x.jpg",
    });
    expect(result?.source).toBe("unknown");
  });
});

describe("normalizeMovieResult", () => {
  it("returns null for non-object input", () => {
    expect(normalizeMovieResult(null)).toBeNull();
    expect(normalizeMovieResult("Heat")).toBeNull();
  });

  it("extracts a minimal movie shape", () => {
    const result = normalizeMovieResult({
      title: "Heat",
      releaseDate: "1995-12-15",
      overview: "A master thief...",
    });
    expect(result?.title).toBe("Heat");
    expect(result?.releaseDate).toBe("1995-12-15");
    expect(result?.overview).toBe("A master thief...");
    expect(result?.posterCandidates).toEqual([]);
  });

  it("normalizes image candidate arrays", () => {
    const result = normalizeMovieResult({
      title: "Heat",
      posterCandidates: [
        { url: "https://image.tmdb.org/t/p/original/poster1.jpg", rank: 9 },
        { url: "https://image.tmdb.org/t/p/original/poster2.jpg", rank: 7 },
        { url: "" }, // dropped
      ],
    });
    expect(result?.posterCandidates.length).toBe(2);
  });

  it("deduplicates tag and cast lists case-insensitively", () => {
    const result = normalizeMovieResult({
      title: "Heat",
      genres: ["Crime", "crime", "Drama"],
      cast: [
        { name: "Al Pacino", character: "Hanna" },
        { name: "AL PACINO", character: "Other" },
        { name: "Robert De Niro" },
      ],
    });
    expect(result?.genres.length).toBe(2);
    expect(result?.cast?.length).toBe(2);
  });

  it("reads external_ids as a string map", () => {
    const result = normalizeMovieResult({
      title: "Heat",
      externalIds: { tmdb: "949", imdb: "tt0113277" },
    });
    expect(result?.externalIds).toEqual({ tmdb: "949", imdb: "tt0113277" });
  });
});

describe("normalizeSeriesResult", () => {
  it("returns null for non-object input", () => {
    expect(normalizeSeriesResult(null)).toBeNull();
  });

  it("extracts a shallow series without seasons", () => {
    const result = normalizeSeriesResult({
      title: "Breaking Bad",
      firstAirDate: "2008-01-20",
      status: "ended",
    });
    expect(result?.title).toBe("Breaking Bad");
    expect(result?.firstAirDate).toBe("2008-01-20");
    expect(result?.status).toBe("ended");
    expect(result?.seasons).toEqual([]);
  });

  it("recursively normalizes nested seasons and episodes (cascade)", () => {
    const result = normalizeSeriesResult({
      title: "Breaking Bad",
      seasons: [
        {
          seasonNumber: 1,
          title: "Season 1",
          episodes: [
            { seasonNumber: 1, episodeNumber: 1, title: "Pilot" },
            { seasonNumber: 1, episodeNumber: 2, title: "Cat's in the Bag" },
          ],
        },
      ],
    });
    expect(result?.seasons.length).toBe(1);
    expect(result?.seasons[0].seasonNumber).toBe(1);
    expect(result?.seasons[0].episodes.length).toBe(2);
    expect(result?.seasons[0].episodes[0].title).toBe("Pilot");
  });

  it("preserves candidate lists for disambiguation", () => {
    const result = normalizeSeriesResult({
      title: "The Office",
      candidates: [
        { externalIds: { tmdb: "2316" }, title: "The Office (US)", year: 2005 },
        { externalIds: { tmdb: "2996" }, title: "The Office (UK)", year: 2001 },
      ],
    });
    expect(result?.candidates?.length).toBe(2);
    expect(result?.candidates?.[0].year).toBe(2005);
  });
});

describe("normalizeSeasonResult", () => {
  it("returns null for non-object input", () => {
    expect(normalizeSeasonResult(null)).toBeNull();
  });

  it("extracts a season shape with episodes", () => {
    const result = normalizeSeasonResult({
      seasonNumber: 1,
      title: "Season 1",
      airDate: "2008-01-20",
      episodes: [{ seasonNumber: 1, episodeNumber: 1, title: "Pilot" }],
    });
    expect(result?.seasonNumber).toBe(1);
    expect(result?.title).toBe("Season 1");
    expect(result?.episodes.length).toBe(1);
  });

  it("defaults seasonNumber to 0 when missing", () => {
    const result = normalizeSeasonResult({ title: "Specials" });
    expect(result?.seasonNumber).toBe(0);
  });
});

describe("normalizeEpisodeResult", () => {
  it("returns null for non-object input", () => {
    expect(normalizeEpisodeResult(null)).toBeNull();
  });

  it("extracts a full episode shape", () => {
    const result = normalizeEpisodeResult({
      seasonNumber: 1,
      episodeNumber: 2,
      absoluteEpisodeNumber: 2,
      title: "Cat's in the Bag",
      airDate: "2008-01-27",
      runtime: 48,
      stillCandidates: [
        { url: "https://image.tmdb.org/t/p/w780/still.jpg" },
      ],
      matched: true,
    });
    expect(result?.seasonNumber).toBe(1);
    expect(result?.episodeNumber).toBe(2);
    expect(result?.absoluteEpisodeNumber).toBe(2);
    expect(result?.title).toBe("Cat's in the Bag");
    expect(result?.runtime).toBe(48);
    expect(result?.stillCandidates.length).toBe(1);
    expect(result?.matched).toBe(true);
  });

  it("defaults matched to undefined when missing", () => {
    const result = normalizeEpisodeResult({
      seasonNumber: 1,
      episodeNumber: 1,
    });
    expect(result?.matched).toBeUndefined();
  });
});
