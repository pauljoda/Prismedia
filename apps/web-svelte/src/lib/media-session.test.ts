import { describe, expect, it } from "vitest";
import { absoluteArtworkUrl, buildMediaArtwork } from "./media-session";

describe("absoluteArtworkUrl", () => {
  it("resolves a root-relative path against the page origin", () => {
    const result = absoluteArtworkUrl("/assets/images/x/cover.jpg");
    expect(result).toBe(`${window.location.origin}/assets/images/x/cover.jpg`);
  });

  it("leaves absolute and data URLs untouched", () => {
    expect(absoluteArtworkUrl("https://cdn.example/cover.jpg")).toBe("https://cdn.example/cover.jpg");
    expect(absoluteArtworkUrl("data:image/png;base64,AAAA")).toBe("data:image/png;base64,AAAA");
  });

  it("returns null for empty input", () => {
    expect(absoluteArtworkUrl(null)).toBeNull();
    expect(absoluteArtworkUrl(undefined)).toBeNull();
    expect(absoluteArtworkUrl("")).toBeNull();
  });
});

describe("buildMediaArtwork", () => {
  it("produces a single absolute entry with an inferred type", () => {
    const artwork = buildMediaArtwork("/assets/a/cover.jpg");
    expect(artwork).toEqual([
      { src: `${window.location.origin}/assets/a/cover.jpg`, sizes: "512x512", type: "image/jpeg" },
    ]);
  });

  it("omits the type when the extension is unknown", () => {
    const artwork = buildMediaArtwork("/assets/a/cover");
    expect(artwork).toEqual([{ src: `${window.location.origin}/assets/a/cover`, sizes: "512x512" }]);
  });

  it("returns an empty list when there is no url", () => {
    expect(buildMediaArtwork(null)).toEqual([]);
  });
});
