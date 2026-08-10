import { cleanup, fireEvent, render, screen } from "@testing-library/svelte";
import { afterEach, describe, expect, it, vi } from "vitest";
import DownloadTreeRows from "./DownloadTreeRows.svelte";
import type { DownloadManagerEntry, DownloadTreeNode } from "./download-tree";

function entry(id: string, title: string): DownloadManagerEntry {
  return {
    row: { acquisitionId: id, title, updatedAt: "2026-08-10T00:00:00Z" } as DownloadManagerEntry["row"],
    item: {
      id,
      title,
      tone: "downloading",
      progress: 0.5,
      statusLabel: "Downloading",
      selectable: true,
    } as DownloadManagerEntry["item"],
  };
}

const child: DownloadTreeNode = {
  key: "entity:season",
  entityId: "season",
  title: "Season 1",
  thumbnail: null,
  directEntryIds: ["one", "two"],
  descendantEntryIds: ["one", "two"],
  children: [],
  activityOrder: 0,
};

const series: DownloadTreeNode = {
  key: "entity:series",
  entityId: "series",
  title: "Sesame Street",
  thumbnail: null,
  directEntryIds: [],
  descendantEntryIds: ["one", "two"],
  children: [child],
  activityOrder: 0,
};

describe("DownloadTreeRows", () => {
  afterEach(cleanup);

  it("selects an Entity group from the row while keeping expansion and bulk selection separate", async () => {
    const onSelect = vi.fn();
    const onToggleExpanded = vi.fn();
    const onSetChecked = vi.fn();
    const entriesById = new Map([
      ["one", entry("one", "Episode one")],
      ["two", entry("two", "Episode two")],
    ]);

    render(DownloadTreeRows, {
      props: {
        nodes: [series],
        entriesById,
        expanded: new Set<string>(),
        selectedKey: null,
        checkedIds: new Set<string>(),
        columnTemplate: "36px 320px 112px 150px 170px 112px 88px 100px 80px",
        onToggleExpanded,
        onSelect,
        onSetChecked,
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Inspect Sesame Street downloads" }));
    expect(onSelect).toHaveBeenCalledWith("entity:series");
    expect(onToggleExpanded).not.toHaveBeenCalled();

    await fireEvent.click(screen.getByRole("checkbox", { name: "Select all Sesame Street downloads" }));
    expect(onSetChecked).toHaveBeenCalledWith(["one", "two"], true);
  });
});
