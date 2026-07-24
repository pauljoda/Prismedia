import { beforeEach, describe, expect, it, vi } from "vitest";

const generated = vi.hoisted(() => ({
  getEntityMonitor: vi.fn(),
  getEntityMonitorEligibility: vi.fn(),
  getEntityMonitorStates: vi.fn(),
  listCutoffUnmetWanted: vi.fn(),
  listEntityMonitors: vi.fn(),
  listMissingWanted: vi.fn(),
  listMonitors: vi.fn(),
  pauseMonitor: vi.fn(),
  resumeMonitor: vi.fn(),
  startEntityMonitor: vi.fn(),
  startMonitor: vi.fn(),
  stopMonitor: vi.fn(),
}));

vi.mock("$lib/api/generated/prismedia", () => generated);

import { fetchEntityMonitors, fetchEntityMonitorStates, startEntityMonitor } from "./monitors";

describe("monitor API", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("does not call the server for an empty child collection", async () => {
    await expect(fetchEntityMonitorStates([])).resolves.toEqual([]);
    expect(generated.getEntityMonitorStates).not.toHaveBeenCalled();
  });

  it("loads every parallel monitor for one Book entity", async () => {
    generated.listEntityMonitors.mockResolvedValue({
      status: 200,
      data: [{ id: "ebook-monitor" }, { id: "audiobook-monitor" }],
    });

    await expect(fetchEntityMonitors("book-1")).resolves.toEqual([
      { id: "ebook-monitor" },
      { id: "audiobook-monitor" },
    ]);
    expect(generated.listEntityMonitors).toHaveBeenCalledWith("book-1");
  });

  it("starts entity monitoring with the selected profile and import target", async () => {
    generated.startEntityMonitor.mockResolvedValue({
      status: 200,
      data: { id: "monitor-1" },
    });

    await startEntityMonitor("series-1", {
      preset: "all",
      profileId: "profile-tv",
      targetLibraryRootId: "library-tv",
    });

    expect(generated.startEntityMonitor).toHaveBeenCalledWith({
      entityId: "series-1",
      preset: "all",
      profileId: "profile-tv",
      targetLibraryRootId: "library-tv",
    });
  });

  it("keeps large child collections within the server batch limit and preserves order", async () => {
    const entityIds = Array.from({ length: 501 }, (_, index) => `entity-${index}`);
    generated.getEntityMonitorStates.mockImplementation(
      async ({ entityIds: batch }: { entityIds: string[] }) => ({
        status: 200,
        data: batch.map((entityId) => ({ entityId })),
      }),
    );

    const states = await fetchEntityMonitorStates(entityIds);

    expect(generated.getEntityMonitorStates).toHaveBeenCalledTimes(2);
    expect(generated.getEntityMonitorStates.mock.calls[0]?.[0].entityIds).toHaveLength(500);
    expect(generated.getEntityMonitorStates.mock.calls[1]?.[0].entityIds).toEqual(["entity-500"]);
    expect(states.map((state) => state.entityId)).toEqual(entityIds);
  });
});
