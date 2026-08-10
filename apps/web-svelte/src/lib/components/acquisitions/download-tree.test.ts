import { describe, expect, it } from "vitest";
import type { EntityThumbnail } from "$lib/api/generated/model";
import type { DownloadManagerEntry } from "./download-tree";
import { buildDownloadTree, expandableDownloadNodeKeys, findDownloadTreeNode } from "./download-tree";

function thumbnail(id: string, title: string, parentEntityId: string | null, sortOrder: number): EntityThumbnail {
  return { id, title, parentEntityId, sortOrder } as EntityThumbnail;
}

function entry(id: string, entityId: string | null, title: string): DownloadManagerEntry {
  return {
    row: { acquisitionId: id, entityId, title } as DownloadManagerEntry["row"],
    item: { id, title } as DownloadManagerEntry["item"],
  };
}

describe("buildDownloadTree", () => {
  it("groups episode downloads through their real series and season ancestors", () => {
    const thumbnails = new Map([
      ["series", thumbnail("series", "Bluey", null, 0)],
      ["season-1", thumbnail("season-1", "Season 1", "series", 1)],
      ["episode-2", thumbnail("episode-2", "Hospital", "season-1", 2)],
      ["episode-1", thumbnail("episode-1", "Magic Xylophone", "season-1", 1)],
    ]);

    const tree = buildDownloadTree([
      entry("download-2", "episode-2", "Hospital"),
      entry("download-1", "episode-1", "Magic Xylophone"),
    ], thumbnails);

    expect(tree).toHaveLength(1);
    expect(tree[0].title).toBe("Bluey");
    expect(tree[0].descendantEntryIds).toEqual(["download-1", "download-2"]);
    expect(tree[0].children[0].title).toBe("Season 1");
    expect(tree[0].children[0].children.map((node) => node.title)).toEqual([
      "Magic Xylophone",
      "Hospital",
    ]);
    expect(expandableDownloadNodeKeys(tree)).toEqual(["entity:series", "entity:season-1"]);
    expect(findDownloadTreeNode(tree, "entity:season-1")?.title).toBe("Season 1");
  });

  it("keeps an acquisition usable when it has not been bound to an Entity", () => {
    const tree = buildDownloadTree([entry("unbound", null, "Manual download")], new Map());
    expect(tree[0]).toMatchObject({
      key: "acquisition:unbound",
      entityId: null,
      directEntryIds: ["unbound"],
      descendantEntryIds: ["unbound"],
    });
  });
});
