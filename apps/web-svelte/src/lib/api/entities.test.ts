import { beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({
  getEntityChildReferences: vi.fn(),
  getEntityChildren: vi.fn(),
}));

vi.mock("$lib/api/generated/prismedia", () => ({
  getEntity: vi.fn(),
  getEntityChildReferences: mocks.getEntityChildReferences,
  getEntityChildren: mocks.getEntityChildren,
  getEntityThumbnails: vi.fn(),
  listEntities: vi.fn(),
  refreshEntity: vi.fn(),
}));

import { fetchEntityChildReferences, fetchEntityChildren } from "$lib/api/entities";

describe("fetchEntityChildren", () => {
  beforeEach(() => {
    mocks.getEntityChildReferences.mockReset();
    mocks.getEntityChildren.mockReset();
  });

  it("deduplicates parents and keeps response groups in server order", async () => {
    const signal = new AbortController().signal;
    mocks.getEntityChildren.mockResolvedValue({
      status: 200,
      data: {
        groups: [
          { parentId: "parent-2", items: [] },
          { parentId: "parent-1", items: [] },
        ],
      },
    });

    const groups = await fetchEntityChildren(
      ["parent-2", "", "parent-1", "parent-2"],
      { hideNsfw: true, signal },
    );

    expect(mocks.getEntityChildren).toHaveBeenCalledWith(
      { parentIds: ["parent-2", "parent-1"] },
      { hideNsfw: true },
      { signal },
    );
    expect(groups.map((group) => group.parentId)).toEqual(["parent-2", "parent-1"]);
  });

  it("chunks requests at the API limit", async () => {
    mocks.getEntityChildren.mockImplementation(async ({ parentIds }: { parentIds: string[] }) => ({
      status: 200,
      data: { groups: parentIds.map((parentId) => ({ parentId, items: [] })) },
    }));
    const ids = Array.from({ length: 251 }, (_, index) => `parent-${index}`);

    const groups = await fetchEntityChildren(ids);

    expect(mocks.getEntityChildren).toHaveBeenCalledTimes(2);
    expect(mocks.getEntityChildren.mock.calls[0]?.[0].parentIds).toHaveLength(250);
    expect(mocks.getEntityChildren.mock.calls[1]?.[0].parentIds).toEqual(["parent-250"]);
    expect(groups).toHaveLength(251);
  });

  it("loads compact child references through the same bounded batches", async () => {
    mocks.getEntityChildReferences.mockImplementation(async ({ parentIds }: { parentIds: string[] }) => ({
      status: 200,
      data: {
        groups: parentIds.map((parentId) => ({
          parentId,
          items: [{ id: `${parentId}-episode`, kind: "video-episode" }],
        })),
      },
    }));
    const ids = Array.from({ length: 251 }, (_, index) => `season-${index}`);

    const groups = await fetchEntityChildReferences(ids, { hideNsfw: true });

    expect(mocks.getEntityChildReferences).toHaveBeenCalledTimes(2);
    expect(mocks.getEntityChildReferences.mock.calls[0]?.[0].parentIds).toHaveLength(250);
    expect(mocks.getEntityChildReferences.mock.calls[1]?.[0].parentIds).toEqual(["season-250"]);
    expect(groups[0]?.items[0]?.id).toBe("season-0-episode");
  });
});
