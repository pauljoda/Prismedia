import { beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({
  getEntity: vi.fn(),
  getEntityChildReferences: vi.fn(),
  getEntityChildren: vi.fn(),
}));

vi.mock("$lib/api/generated/prismedia", () => ({
  getEntity: mocks.getEntity,
  getEntityChildReferences: mocks.getEntityChildReferences,
  getEntityChildren: mocks.getEntityChildren,
  getEntityThumbnails: vi.fn(),
  listEntities: vi.fn(),
  refreshEntity: vi.fn(),
}));

import { ENTITY_KIND } from "$lib/api/generated/codes";
import {
  fetchEntity,
  fetchEntityChildReferences,
  fetchEntityChildren,
  fetchEntityWithChildren,
} from "$lib/api/entities";

describe("fetchEntityChildren", () => {
  beforeEach(() => {
    mocks.getEntityChildReferences.mockReset();
    mocks.getEntityChildren.mockReset();
    mocks.getEntity.mockReset();
  });

  it("forwards explicit NSFW visibility to entity detail reads", async () => {
    const signal = new AbortController().signal;
    mocks.getEntity.mockResolvedValue({ status: 200, data: { id: "entity" } });

    await fetchEntity("entity", { hideNsfw: false, signal });

    expect(mocks.getEntity).toHaveBeenCalledWith("entity", { hideNsfw: false }, { signal });
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

describe("fetchEntityWithChildren", () => {
  beforeEach(() => {
    mocks.getEntity.mockReset();
    mocks.getEntityChildren.mockReset();
  });

  const child = (id: string, kind: string) => ({ id, kind });

  it("replaces paged child groups with every child from the batch read", async () => {
    const signal = new AbortController().signal;
    mocks.getEntity.mockResolvedValue({
      status: 200,
      data: {
        id: "gallery",
        childrenByKind: [
          { kind: ENTITY_KIND.gallery, label: "Galleries", entities: [child("sub", ENTITY_KIND.gallery)] },
          {
            kind: ENTITY_KIND.image,
            label: "Images",
            entities: [child("image-0", ENTITY_KIND.image)],
            totalCount: 3,
          },
        ],
      },
    });
    mocks.getEntityChildren.mockResolvedValue({
      status: 200,
      data: {
        groups: [{
          parentId: "gallery",
          items: [
            child("sub", ENTITY_KIND.gallery),
            child("image-0", ENTITY_KIND.image),
            child("image-1", ENTITY_KIND.image),
            child("image-2", ENTITY_KIND.image),
          ],
        }],
      },
    });

    const gallery = await fetchEntityWithChildren("gallery", { hideNsfw: true, signal });

    expect(mocks.getEntityChildren).toHaveBeenCalledWith(
      { parentIds: ["gallery"] },
      { hideNsfw: true },
      { signal },
    );
    expect(gallery.childrenByKind.map((group) => group.entities.map((entity) => entity.id))).toEqual([
      ["sub"],
      ["image-0", "image-1", "image-2"],
    ]);
    expect(gallery.childrenByKind[1]?.totalCount).toBeNull();
  });

  it("skips the batch read when every child group is complete", async () => {
    mocks.getEntity.mockResolvedValue({
      status: 200,
      data: {
        id: "album",
        childrenByKind: [{
          kind: ENTITY_KIND.audioTrack,
          label: "Tracks",
          entities: [child("track", ENTITY_KIND.audioTrack)],
          totalCount: null,
        }],
      },
    });

    await fetchEntityWithChildren("album");

    expect(mocks.getEntityChildren).not.toHaveBeenCalled();
  });
});
