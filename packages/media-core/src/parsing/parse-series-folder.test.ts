import { describe, it, expect } from "vitest";
import { parseSeriesFolder } from "./parse-series-folder";

describe("parseSeriesFolder", () => {
  it("returns the folder name as the title with no year", () => {
    expect(parseSeriesFolder("Breaking Bad")).toEqual({
      title: "Breaking Bad",
      year: null,
    });
  });

  it("extracts year from trailing (YYYY)", () => {
    expect(parseSeriesFolder("The Office (2005)")).toEqual({
      title: "The Office",
      year: 2005,
    });
  });

  it("extracts year from trailing [YYYY]", () => {
    expect(parseSeriesFolder("Westworld [2016]")).toEqual({
      title: "Westworld",
      year: 2016,
    });
  });

  it("collapses dot and underscore separators into spaces", () => {
    expect(parseSeriesFolder("Breaking.Bad")).toEqual({
      title: "Breaking Bad",
      year: null,
    });
    expect(parseSeriesFolder("Breaking_Bad")).toEqual({
      title: "Breaking Bad",
      year: null,
    });
  });

  it("strips common release-group suffixes", () => {
    expect(parseSeriesFolder("Breaking.Bad.1080p.WEB-DL.x264-GROUP")).toEqual({
      title: "Breaking Bad",
      year: null,
    });
    expect(parseSeriesFolder("The Expanse 2160p HDR x265")).toEqual({
      title: "The Expanse",
      year: null,
    });
  });

  it("keeps year when present alongside release tags", () => {
    expect(parseSeriesFolder("The.Office.2005.1080p.WEB-DL")).toEqual({
      title: "The Office",
      year: 2005,
    });
  });

  it("collapses repeated whitespace", () => {
    expect(parseSeriesFolder("The    Expanse")).toEqual({
      title: "The Expanse",
      year: null,
    });
  });

  it("handles empty string", () => {
    expect(parseSeriesFolder("")).toEqual({ title: "", year: null });
  });
});
