import { describe, expect, it, vi } from "vitest";
import {
  ACQUISITION_STATUS,
  ENTITY_KIND,
  MONITOR_STATUS,
  type AcquisitionStatusCode,
} from "$lib/api/generated/codes";
import type { DownloadQueueItemView, WantedListItemView } from "$lib/api/generated/model";
import { downloadToListItem, wantedToListItem } from "./acquisition-list-item";

describe("download acquisition list items", () => {
  it("locks every destructive or restart action while cleanup is in progress", () => {
    const item = downloadToListItem(
      download(ACQUISITION_STATUS.stopping),
      null,
      { onReSearch: vi.fn(), onRemove: vi.fn() },
      false,
    );

    expect(item.statusLabel).toBe("Cleaning up");
    expect(item.tone).toBe("cleanup");
    expect(item.indeterminate).toBe(true);
    expect(item.description).toBe("Removing download and managed files…");
    expect(item.selectable).toBe(false);
    expect(item.primaryAction).toBeNull();
    expect(item.removeAction).toBeNull();
    expect(item.menuActions).toEqual([]);
  });

  it("fails closed when the server returns a newer lifecycle state", () => {
    const unknown = "future-lifecycle-state" as AcquisitionStatusCode;
    const item = downloadToListItem(
      download(unknown),
      null,
      { onReSearch: vi.fn(), onRemove: vi.fn() },
      false,
    );

    expect(item.statusLabel).toBe("Updating");
    expect(item.tone).toBe("cleanup");
    expect(item.selectable).toBe(false);
    expect(item.primaryAction).toBeNull();
    expect(item.removeAction).toBeNull();
    expect(item.menuActions).toEqual([]);
  });

  it("locks wanted-list actions while monitor cleanup owns the Entity", () => {
    const item = wantedToListItem(
      wanted(MONITOR_STATUS.deletingFiles),
      "missing",
      null,
      { onSearchNow: vi.fn(), onUnmonitor: vi.fn() },
      false,
    );

    expect(item.statusLabel).toBe("Deleting files");
    expect(item.tone).toBe("cleanup");
    expect(item.selectable).toBe(false);
    expect(item.primaryAction).toBeNull();
    expect(item.removeAction).toBeNull();
    expect(item.menuActions).toEqual([]);
  });
});

function download(status: AcquisitionStatusCode): DownloadQueueItemView {
  return {
    acquisitionId: "acquisition-1",
    entityId: "movie-1",
    kind: ENTITY_KIND.movie,
    title: "Arrival",
    status,
    statusMessage: null,
    progress: null,
    updatedAt: "2026-07-10T00:00:00Z",
  };
}

function wanted(status: WantedListItemView["monitorStatus"]): WantedListItemView {
  return {
    monitorId: "monitor-1",
    acquisitionId: "acquisition-1",
    entityId: "movie-1",
    kind: ENTITY_KIND.movie,
    title: "Arrival",
    monitorStatus: status,
    acquisitionStatus: null,
    lastSearchedAt: null,
    nextSearchAt: null,
    ownedQuality: null,
    cutoffQuality: null,
    barrenSearches: 0,
  };
}
