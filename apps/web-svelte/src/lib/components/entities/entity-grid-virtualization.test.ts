import { describe, expect, it } from "vitest";
import {
  chunkEntityGridRows,
  computeEntityGridVirtualWindow,
  resolveEntityGridColumnCount,
} from "./entity-grid-virtualization";

describe("entity grid virtualization", () => {
  it("resolves the same responsive column counts as the thumbnail grid CSS", () => {
    expect(resolveEntityGridColumnCount({ scale: 5, viewportWidth: 480, viewMode: "grid", mediaWall: false })).toBe(4);
    expect(resolveEntityGridColumnCount({ scale: 5, viewportWidth: 800, viewMode: "grid", mediaWall: false })).toBe(4);
    expect(resolveEntityGridColumnCount({ scale: 7, viewportWidth: 1280, viewMode: "grid", mediaWall: false })).toBe(7);
    expect(resolveEntityGridColumnCount({ scale: 7, viewportWidth: 480, viewMode: "grid", mediaWall: true })).toBe(7);
    expect(resolveEntityGridColumnCount({ scale: 7, viewportWidth: 1280, viewMode: "list", mediaWall: false })).toBe(1);
  });

  it("chunks cards into stable rows with their source page index", () => {
    const rows = chunkEntityGridRows(["a", "b", "c", "d", "e"], 2);

    expect(rows).toEqual([
      { index: 0, startIndex: 0, items: ["a", "b"] },
      { index: 1, startIndex: 2, items: ["c", "d"] },
      { index: 2, startIndex: 4, items: ["e"] },
    ]);
  });

  it("returns an overscanned row window with spacer heights", () => {
    const window = computeEntityGridVirtualWindow({
      rowCount: 100,
      rowHeights: {
        0: 100,
        1: 100,
        2: 100,
        3: 100,
        4: 100,
        5: 100,
      },
      estimatedRowHeight: 100,
      rowGap: 10,
      scrollOffset: 1_000,
      viewportHeight: 300,
      overscan: 200,
    });

    expect(window.startRow).toBe(7);
    expect(window.endRow).toBe(14);
    expect(window.beforeHeight).toBe(770);
    expect(window.afterHeight).toBe(9_450);
    expect(window.visibleRowCount).toBe(7);
  });

  it("keeps at least the last row renderable when estimates lag behind a fast jump", () => {
    const window = computeEntityGridVirtualWindow({
      rowCount: 4,
      rowHeights: {},
      estimatedRowHeight: 100,
      rowGap: 10,
      scrollOffset: 10_000,
      viewportHeight: 300,
      overscan: 200,
    });

    expect(window.startRow).toBe(3);
    expect(window.endRow).toBe(4);
    expect(window.visibleRowCount).toBe(1);
  });
});
