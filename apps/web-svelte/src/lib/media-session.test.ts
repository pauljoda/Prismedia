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
  it("emits absolute, well-formed entries at multiple sizes with an inferred type", () => {
    const artwork = buildMediaArtwork("/assets/a/cover.jpg");
    const src = `${window.location.origin}/assets/a/cover.jpg`;
    expect(artwork.length).toBeGreaterThan(1);
    expect(artwork).toContainEqual({ src, sizes: "512x512", type: "image/jpeg" });
    // Every entry is a single-token size (no space-separated list, which WebKit rejects).
    for (const entry of artwork) {
      expect(entry.src).toBe(src);
      expect(entry.type).toBe("image/jpeg");
      expect(entry.sizes).toMatch(/^\d+x\d+$/);
    }
  });

  it("omits the type when the extension is unknown", () => {
    const artwork = buildMediaArtwork("/assets/a/cover");
    expect(artwork.every((entry) => !("type" in entry))).toBe(true);
    expect(artwork[0]?.src).toBe(`${window.location.origin}/assets/a/cover`);
  });

  it("returns an empty list when there is no url", () => {
    expect(buildMediaArtwork(null)).toEqual([]);
  });
});
