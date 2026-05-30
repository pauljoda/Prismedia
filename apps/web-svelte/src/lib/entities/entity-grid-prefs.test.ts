import { afterEach, describe, expect, it } from "vitest";
import { createEntityGridPrefs, type EntityGridPrefsDefaults } from "./entity-grid-prefs";
import { readCookie } from "$lib/utils/cookie";

const DEFAULTS: EntityGridPrefsDefaults = {
  sortBy: "added",
  sortDir: "desc",
  mediaWall: false,
  scale: 5,
  pageSize: 250,
};

function clearAllCookies(): void {
  for (const entry of document.cookie.split(";")) {
    const name = entry.split("=")[0]?.trim();
    if (name) document.cookie = `${name}=;path=/;max-age=0;samesite=lax`;
  }
}

describe("entity-grid prefs", () => {
  afterEach(() => clearAllCookies());

  it("derives a sanitized, namespaced cookie name from the grid key", () => {
    const api = createEntityGridPrefs("series-abc/123", DEFAULTS);
    expect(api.cookieName).toBe("pm_eg_series-abc_123");
  });

  it("round-trips the full view state through the cookie", () => {
    const api = createEntityGridPrefs("videos", DEFAULTS);
    api.writeCookie({
      query: "matrix",
      activeKind: "video",
      filterIds: ["flags:favorite", "rating:min:4"],
      includeNsfw: false,
      sortBy: "rating",
      sortDir: "asc",
      viewMode: "list",
      mediaWall: true,
      scale: 8,
      pageSize: 100,
      activePresetId: "preset-1",
    });

    expect(api.parse(readCookie(api.cookieName))).toEqual({
      query: "matrix",
      activeKind: "video",
      filterIds: ["flags:favorite", "rating:min:4"],
      includeNsfw: false,
      sortBy: "rating",
      sortDir: "asc",
      viewMode: "list",
      mediaWall: true,
      scale: 8,
      pageSize: 100,
      activePresetId: "preset-1",
    });
  });

  it("fills missing or malformed fields from the grid defaults", () => {
    const api = createEntityGridPrefs("books", DEFAULTS);
    const parsed = api.parse(
      encodeURIComponent(JSON.stringify({ scale: 9, sortBy: "bogus", filterIds: ["ok", 7] })),
    );

    expect(parsed).toEqual({
      query: "",
      activeKind: "all",
      filterIds: ["ok"],
      includeNsfw: true,
      sortBy: "added",
      sortDir: "desc",
      viewMode: "grid",
      mediaWall: false,
      scale: 9,
      pageSize: 250,
      activePresetId: null,
    });
  });

  it("treats the seeded default state as default so the cookie can be cleared", () => {
    const api = createEntityGridPrefs("galleries", DEFAULTS);
    expect(api.isDefault(api.defaults())).toBe(true);
    expect(api.isDefault({ ...api.defaults(), filterIds: ["flags:favorite"] })).toBe(false);
  });

  it("returns null for an absent cookie", () => {
    const api = createEntityGridPrefs("audio", DEFAULTS);
    expect(api.parse(readCookie(api.cookieName))).toBeNull();
  });
});
