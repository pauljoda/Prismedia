import { describe, expect, it } from "vitest";
import type { DownloadManagerEntry, DownloadTreeNode } from "./download-tree";
import {
  DEFAULT_DOWNLOAD_COLUMN_WIDTHS,
  clampDownloadColumnWidth,
  downloadColumnTemplate,
  sortDownloadTree,
} from "./download-table";

function entry(id: string, title: string, size: number, speed: number): DownloadManagerEntry {
  return {
    row: { acquisitionId: id, title, totalSizeBytes: size, downloadSpeedBytesPerSecond: speed, updatedAt: "2026-08-10T00:00:00Z" } as DownloadManagerEntry["row"],
    item: { id, title, tone: "downloading", progress: 0.5 } as DownloadManagerEntry["item"],
  };
}

function node(key: string, title: string, ids: string[], children: DownloadTreeNode[] = []): DownloadTreeNode {
  return { key, entityId: key, title, thumbnail: null, directEntryIds: ids, descendantEntryIds: [...ids, ...children.flatMap((child) => child.descendantEntryIds)], children, activityOrder: 0 };
}

describe("download table", () => {
  it("sorts sibling branches by aggregate transfer data without flattening the tree", () => {
    const entries = new Map([
      ["slow", entry("slow", "Slow episode", 10, 2)],
      ["fast", entry("fast", "Fast episode", 20, 9)],
    ]);
    const tree = [node("series", "Series", [], [
      node("slow-season", "Season 1", ["slow"]),
      node("fast-season", "Season 2", ["fast"]),
    ])];

    const sorted = sortDownloadTree(tree, entries, "speed", "desc");
    expect(sorted).toHaveLength(1);
    expect(sorted[0].children.map((child) => child.title)).toEqual(["Season 2", "Season 1"]);
  });

  it("clamps resized columns to their definition-owned limits", () => {
    expect(clampDownloadColumnWidth("entity", 10)).toBe(220);
    expect(clampDownloadColumnWidth("entity", 1900)).toBe(1600);
  });

  it("lets the identity track absorb spare viewport width", () => {
    expect(downloadColumnTemplate(DEFAULT_DOWNLOAD_COLUMN_WIDTHS)).toContain("minmax(320px, 1fr)");
  });
});
