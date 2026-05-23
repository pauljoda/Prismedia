import { describe, expect, it, vi } from "vitest";
import { PageSnapshotsStore } from "./page-snapshots.svelte";

describe("PageSnapshotsStore", () => {
  it("restores pending surface snapshots when the surface registers after history restore", () => {
    const restoreScroll = vi.fn();
    const store = new PageSnapshotsStore({
      captureScroll: () => ({ top: 900, left: 0 }),
      restoreScroll,
    });

    store.restore({
      scroll: { top: 900, left: 0 },
      surfaces: {
        videos: {
          items: [{ id: "video-1" }, { id: "video-2" }],
          total: 200,
          loadedStart: 0,
          selectedIds: ["video-2"],
        },
      },
    });

    const restore = vi.fn();
    store.registerSurface("videos", {
      capture: () => ({ items: [], total: 0, loadedStart: 0, selectedIds: [] }),
      restore,
    });

    expect(restore).toHaveBeenCalledWith({
      items: [{ id: "video-1" }, { id: "video-2" }],
      total: 200,
      loadedStart: 0,
      selectedIds: ["video-2"],
    });
    expect(restoreScroll).toHaveBeenCalledWith({ top: 900, left: 0 });
  });
});
