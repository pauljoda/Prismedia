import { describe, it, expect } from "vitest";
import { parseSeasonFolder } from "./parse-season-folder";

describe("parseSeasonFolder", () => {
  it("parses 'Season 1' style", () => {
    expect(parseSeasonFolder("Season 1")).toEqual({ seasonNumber: 1, title: null });
    expect(parseSeasonFolder("Season 01")).toEqual({ seasonNumber: 1, title: null });
    expect(parseSeasonFolder("Season_1")).toEqual({ seasonNumber: 1, title: null });
    expect(parseSeasonFolder("season.1")).toEqual({ seasonNumber: 1, title: null });
  });

  it("parses 'S01' style", () => {
    expect(parseSeasonFolder("S1")).toEqual({ seasonNumber: 1, title: null });
    expect(parseSeasonFolder("S01")).toEqual({ seasonNumber: 1, title: null });
    expect(parseSeasonFolder("S 01")).toEqual({ seasonNumber: 1, title: null });
    expect(parseSeasonFolder("S10")).toEqual({ seasonNumber: 10, title: null });
  });

  it("parses non-English variants", () => {
    expect(parseSeasonFolder("Saison 1")).toEqual({ seasonNumber: 1, title: null });
    expect(parseSeasonFolder("Temporada 2")).toEqual({ seasonNumber: 2, title: null });
    expect(parseSeasonFolder("Staffel 3")).toEqual({ seasonNumber: 3, title: null });
  });

  it("maps Specials/Special/Extras/OVA to season 0", () => {
    expect(parseSeasonFolder("Specials")).toEqual({ seasonNumber: 0, title: "Specials" });
    expect(parseSeasonFolder("Special")).toEqual({ seasonNumber: 0, title: "Specials" });
    expect(parseSeasonFolder("Extras")).toEqual({ seasonNumber: 0, title: "Specials" });
    expect(parseSeasonFolder("OVA")).toEqual({ seasonNumber: 0, title: "Specials" });
  });

  it("parses bare-number folders", () => {
    expect(parseSeasonFolder("1")).toEqual({ seasonNumber: 1, title: null });
    expect(parseSeasonFolder("01")).toEqual({ seasonNumber: 1, title: null });
    expect(parseSeasonFolder("12")).toEqual({ seasonNumber: 12, title: null });
  });

  it("returns nulls for unrecognized folder names", () => {
    expect(parseSeasonFolder("Breaking Bad")).toEqual({ seasonNumber: null, title: null });
    expect(parseSeasonFolder("Behind The Scenes")).toEqual({ seasonNumber: null, title: null });
    expect(parseSeasonFolder("")).toEqual({ seasonNumber: null, title: null });
  });

  it("is case-insensitive", () => {
    expect(parseSeasonFolder("season 1")).toEqual({ seasonNumber: 1, title: null });
    expect(parseSeasonFolder("SEASON 01")).toEqual({ seasonNumber: 1, title: null });
    expect(parseSeasonFolder("s01")).toEqual({ seasonNumber: 1, title: null });
    expect(parseSeasonFolder("specials")).toEqual({ seasonNumber: 0, title: "Specials" });
  });

  it("trims surrounding whitespace", () => {
    expect(parseSeasonFolder("  Season 1  ")).toEqual({ seasonNumber: 1, title: null });
  });
});
