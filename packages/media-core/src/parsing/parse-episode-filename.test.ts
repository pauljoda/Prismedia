import { describe, it, expect } from "vitest";
import { parseEpisodeFilename } from "./parse-episode-filename";

describe("parseEpisodeFilename", () => {
  it("parses S01E02 style", () => {
    expect(parseEpisodeFilename("Show.Name.S01E02.mkv")).toEqual({
      seasonNumber: 1,
      episodeNumber: 2,
      absoluteEpisodeNumber: null,
      title: null,
      year: null,
    });
  });

  it("parses S01E02 with title", () => {
    expect(parseEpisodeFilename("Breaking Bad - S01E02 - Cat's in the Bag.mkv")).toEqual({
      seasonNumber: 1,
      episodeNumber: 2,
      absoluteEpisodeNumber: null,
      title: "Cat's in the Bag",
      year: null,
    });
  });

  it("parses s1e2 lowercase", () => {
    expect(parseEpisodeFilename("show.s1e2.mkv")).toMatchObject({
      seasonNumber: 1,
      episodeNumber: 2,
    });
  });

  it("parses S01.E02 dotted", () => {
    expect(parseEpisodeFilename("Show.S01.E02.1080p.mkv")).toMatchObject({
      seasonNumber: 1,
      episodeNumber: 2,
    });
  });

  it("parses 1x02 style", () => {
    expect(parseEpisodeFilename("Show - 1x02 - Title.mkv")).toMatchObject({
      seasonNumber: 1,
      episodeNumber: 2,
      title: "Title",
    });
  });

  it("parses 01x02 style", () => {
    expect(parseEpisodeFilename("Show 01x02.mkv")).toMatchObject({
      seasonNumber: 1,
      episodeNumber: 2,
    });
  });

  it("parses 'Season 1 Episode 2' long form", () => {
    expect(parseEpisodeFilename("Show Season 1 Episode 2.mkv")).toMatchObject({
      seasonNumber: 1,
      episodeNumber: 2,
    });
    expect(parseEpisodeFilename("Show - Season 1 - Episode 2.mkv")).toMatchObject({
      seasonNumber: 1,
      episodeNumber: 2,
    });
  });

  it("parses bare absolute episode numbers (anime convention)", () => {
    expect(parseEpisodeFilename("One Piece - 005 - The Curse of Demon Sword.mkv")).toEqual({
      seasonNumber: null,
      episodeNumber: null,
      absoluteEpisodeNumber: 5,
      title: "The Curse of Demon Sword",
      year: null,
    });
  });

  it("parses bare absolute episode numbers with dot separators", () => {
    expect(parseEpisodeFilename("One.Piece.005.1080p.mkv")).toMatchObject({
      absoluteEpisodeNumber: 5,
    });
  });

  it("does not treat resolution as an episode number", () => {
    expect(parseEpisodeFilename("Show.1080p.mkv")).toEqual({
      seasonNumber: null,
      episodeNumber: null,
      absoluteEpisodeNumber: null,
      title: null,
      year: null,
    });
  });

  it("extracts year from filename", () => {
    expect(parseEpisodeFilename("Show.S01E02.2019.mkv")).toMatchObject({
      seasonNumber: 1,
      episodeNumber: 2,
      year: 2019,
    });
    expect(parseEpisodeFilename("Show (2019) - S01E02.mkv")).toMatchObject({
      seasonNumber: 1,
      episodeNumber: 2,
      year: 2019,
    });
  });

  it("returns all nulls for unrecognized filenames", () => {
    expect(parseEpisodeFilename("random garbage.mkv")).toEqual({
      seasonNumber: null,
      episodeNumber: null,
      absoluteEpisodeNumber: null,
      title: null,
      year: null,
    });
  });

  it("ignores file extensions", () => {
    expect(parseEpisodeFilename("Show.S01E02")).toMatchObject({
      seasonNumber: 1,
      episodeNumber: 2,
    });
    expect(parseEpisodeFilename("Show.S01E02.mp4")).toMatchObject({
      seasonNumber: 1,
      episodeNumber: 2,
    });
  });

  it("handles an absolute path argument", () => {
    expect(parseEpisodeFilename("/media/tv/Show/Season 1/Show.S01E02.mkv")).toMatchObject({
      seasonNumber: 1,
      episodeNumber: 2,
    });
  });
});
