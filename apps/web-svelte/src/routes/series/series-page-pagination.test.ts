import { describe, expect, it } from "vitest";
import {
  applySeriesPageMerge,
  getSeriesLoadedWindow,
  seriesPageHref,
} from "./series-page-pagination";
import {
  getSelectionState,
  selectAllVisibleIds,
  toggleSelectedId,
} from "./series-page-selection";

describe("series page pagination helpers", () => {
  it("computes loaded windows and next page numbers from SSR offsets", () => {
    expect(
      getSeriesLoadedWindow({
        loadedStart: 60,
        itemCount: 30,
        total: 130,
        pageSize: 60,
      }),
    ).toEqual({
      loadedEnd: 90,
      hasMore: true,
      nextPageNumber: 2,
    });
  });

  it("preserves existing query params while building next-page hrefs", () => {
    const url = new URL("http://localhost/series?series=s1&season=2&page=2");

    expect(seriesPageHref(url, 3)).toBe("/series?series=s1&season=2&page=3");
    expect(seriesPageHref(url, 1)).toBe("/series?series=s1&season=2");
  });

  it("merges loaded pages without duplicating existing entities", () => {
    const merged = applySeriesPageMerge({
      current: [{ id: "a" }, { id: "b" }],
      incoming: [{ id: "b" }, { id: "c" }],
      loadedStart: 0,
      total: 3,
    });

    expect(merged.items).toEqual([{ id: "a" }, { id: "b" }, { id: "c" }]);
    expect(merged.total).toBe(3);
  });
});

describe("series page selection helpers", () => {
  it("toggles selected ids immutably", () => {
    const first = toggleSelectedId(new Set(), "a");
    const second = toggleSelectedId(first, "a");

    expect([...first]).toEqual(["a"]);
    expect([...second]).toEqual([]);
  });

  it("selects or clears all visible ids based on current state", () => {
    const visible = ["a", "b"];

    expect([...selectAllVisibleIds(new Set(["a"]), visible, false)]).toEqual(["a", "b"]);
    expect([...selectAllVisibleIds(new Set(["a", "b"]), visible, true)]).toEqual([]);
  });

  it("summarizes visible selection state", () => {
    expect(getSelectionState(new Set(["a", "c"]), ["a", "b"])).toEqual({
      visibleCount: 2,
      allVisibleSelected: false,
    });
    expect(getSelectionState(new Set(["a", "b"]), ["a", "b"])).toEqual({
      visibleCount: 2,
      allVisibleSelected: true,
    });
  });
});
