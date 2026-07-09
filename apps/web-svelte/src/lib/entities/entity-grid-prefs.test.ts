import { afterEach, describe, expect, it } from "vitest";
import { createEntityGridPrefs, type EntityGridPrefsDefaults } from "./entity-grid-prefs";

const DEFAULTS: EntityGridPrefsDefaults = {
  sortBy: "added",
  sortDir: "desc",
  mediaWall: false,
  scale: 5,
  pageSize: 250,
};

describe("entity-grid prefs", () => {
  afterEach(() => window.localStorage.clear());

  it("derives a namespaced storage key from the grid key", () => {
    const store = createEntityGridPrefs("series-abc-123", DEFAULTS);
    expect(store.storageKey).toBe("prismedia:entity-grid-state:series-abc-123");
  });

  it("round-trips the full view state through localStorage", () => {
    const store = createEntityGridPrefs("videos", DEFAULTS);
    store.save({
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
      barsCollapsed: true,
    });

    expect(store.load()).toEqual({
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
      barsCollapsed: true,
    });
  });

  it("fills missing or malformed fields from the grid defaults", () => {
    const store = createEntityGridPrefs("books", DEFAULTS);
    window.localStorage.setItem(
      store.storageKey,
      JSON.stringify({ scale: 9, sortBy: "bogus", filterIds: ["ok", 7] }),
    );

    expect(store.load()).toEqual({
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
      barsCollapsed: false,
    });
  });

  it("migrates legacy file filters to the availability family", () => {
    const store = createEntityGridPrefs("series", DEFAULTS);
    window.localStorage.setItem(
      store.storageKey,
      JSON.stringify({ filterIds: ["files:has:true", "files:has:false"] }),
    );

    expect(store.load()?.filterIds).toEqual(["availability:on-disk", "availability:wanted"]);
  });

  it("treats the seeded default state as default so the entry can be cleared", () => {
    const store = createEntityGridPrefs("galleries", DEFAULTS);
    expect(store.isDefault(store.defaults())).toBe(true);
    expect(store.isDefault({ ...store.defaults(), filterIds: ["flags:favorite"] })).toBe(false);
  });

  it("clears stored state", () => {
    const store = createEntityGridPrefs("studios", DEFAULTS);
    store.save({ ...store.defaults(), scale: 9 });
    store.clear();
    expect(store.load()).toBeNull();
  });

  it("returns null when nothing is stored", () => {
    const store = createEntityGridPrefs("audio", DEFAULTS);
    expect(store.load()).toBeNull();
  });

  it("returns null for unparseable stored state", () => {
    const store = createEntityGridPrefs("people", DEFAULTS);
    window.localStorage.setItem(store.storageKey, "not json");
    expect(store.load()).toBeNull();
  });
});
