import { describe, expect, it } from "vitest";
import { shouldPreferNativeHLS } from "./native-hls";

describe("native HLS preference", () => {
  it("keeps desktop Chromium on hls.js even when its user agent contains AppleWebKit", () => {
    expect(shouldPreferNativeHLS({
      vendor: "Google Inc.",
      userAgent: "Mozilla/5.0 AppleWebKit/537.36 Chrome/147.0.0.0 Safari/537.36",
    })).toBe(false);
  });

  it("uses native HLS for Apple WebKit browsers", () => {
    expect(shouldPreferNativeHLS({
      vendor: "Apple Computer, Inc.",
      userAgent: "Mozilla/5.0 AppleWebKit/605.1.15 Version/26.0 Safari/605.1.15",
    })).toBe(true);
    expect(shouldPreferNativeHLS({
      vendor: "Apple Computer, Inc.",
      userAgent: "Mozilla/5.0 (iPhone) AppleWebKit/605.1.15 CriOS/147.0 Mobile/15E148 Safari/604.1",
    })).toBe(true);
  });

  it("does not infer Apple WebKit from either signal alone", () => {
    expect(shouldPreferNativeHLS({ vendor: "Apple Computer, Inc.", userAgent: "" })).toBe(false);
    expect(shouldPreferNativeHLS({ vendor: "", userAgent: "AppleWebKit/605.1.15" })).toBe(false);
    expect(shouldPreferNativeHLS(null)).toBe(false);
  });
});
