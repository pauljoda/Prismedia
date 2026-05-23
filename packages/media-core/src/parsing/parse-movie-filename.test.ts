import { describe, it, expect } from "vitest";
import { parseMovieFilename } from "./parse-movie-filename";

describe("parseMovieFilename", () => {
  it("returns the cleaned filename as title with no year", () => {
    expect(parseMovieFilename("Heat.mkv")).toEqual({ title: "Heat", year: null });
  });

  it("extracts year from (YYYY)", () => {
    expect(parseMovieFilename("Blade Runner (1982).mkv")).toEqual({
      title: "Blade Runner",
      year: 1982,
    });
  });

  it("extracts year from bare YYYY with separator context", () => {
    expect(parseMovieFilename("Inception.2010.1080p.BluRay.x264.mkv")).toEqual({
      title: "Inception",
      year: 2010,
    });
  });

  it("extracts year from [YYYY]", () => {
    expect(parseMovieFilename("Arrival [2016].mkv")).toEqual({
      title: "Arrival",
      year: 2016,
    });
  });

  it("strips release-group suffixes and resolution", () => {
    expect(parseMovieFilename("The Dark Knight.2008.2160p.HDR.HEVC-GROUP.mkv")).toEqual({
      title: "The Dark Knight",
      year: 2008,
    });
  });

  it("collapses dots and underscores into spaces", () => {
    expect(parseMovieFilename("Mad_Max_Fury_Road.2015.mkv")).toEqual({
      title: "Mad Max Fury Road",
      year: 2015,
    });
  });

  it("handles absolute paths", () => {
    expect(parseMovieFilename("/media/movies/Heat (1995).mkv")).toEqual({
      title: "Heat",
      year: 1995,
    });
  });

  it("returns empty title for empty input", () => {
    expect(parseMovieFilename("")).toEqual({ title: "", year: null });
  });
});
