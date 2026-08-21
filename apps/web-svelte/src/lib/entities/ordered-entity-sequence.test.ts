import { beforeEach, describe, expect, it, vi } from "vitest";
import type { EntityThumbnail } from "$lib/api/generated/model";
import type { EntityCardFull } from "$lib/api/entities";
import {
  CAPABILITY_KIND,
  ENTITY_KIND,
} from "$lib/entities/entity-codes";
import { ENTITY_SEQUENCE_ROLE } from "$lib/api/generated/codes";

const mocks = vi.hoisted(() => ({
  fetchEntity: vi.fn(),
  fetchOrderedEntityThumbnails: vi.fn(),
}));

vi.mock("$lib/api/entities", () => ({
  fetchEntity: mocks.fetchEntity,
}));

vi.mock("$lib/entities/entity-relationship-thumbnails", () => ({
  fetchOrderedEntityThumbnails: mocks.fetchOrderedEntityThumbnails,
}));

import { resolveOrderedEntitySequence } from "./ordered-entity-sequence";

describe("ordered Entity sequences", () => {
  beforeEach(() => {
    mocks.fetchEntity.mockReset();
    mocks.fetchOrderedEntityThumbnails.mockReset();
  });

  it("keeps direct items in their own root scope", async () => {
    const root = detail("series", ENTITY_KIND.comicSeries, null, 0, {
      [ENTITY_KIND.comicInstallment]: ["direct-2", "direct-1"],
      [ENTITY_KIND.comicVolume]: ["volume-1"],
    });
    const selected = sequenceItem("direct-1", "series", 1);
    const thumbnails = new Map([
      ["direct-1", thumbnail("direct-1", "Direct 1", 1, "series")],
      ["direct-2", thumbnail("direct-2", "Direct 2", 2, "series")],
    ]);
    mocks.fetchEntity.mockResolvedValue(root);
    mocks.fetchOrderedEntityThumbnails.mockImplementation(async (ids: string[]) =>
      ids.flatMap((id) => thumbnails.get(id) ?? []));

    const result = await resolveOrderedEntitySequence(selected);

    expect(result?.items.map((item) => item.id)).toEqual(["direct-1", "direct-2"]);
    expect(mocks.fetchEntity).toHaveBeenCalledTimes(1);
    expect(mocks.fetchEntity).not.toHaveBeenCalledWith("volume-1", undefined);
  });

  it("orders grouped items by container before their local item order", async () => {
    const root = detail("series", ENTITY_KIND.comicSeries, null, 0, {
      [ENTITY_KIND.comicInstallment]: ["direct-1"],
      [ENTITY_KIND.comicVolume]: ["volume-2", "volume-1"],
    });
    const volumeOne = detail("volume-1", ENTITY_KIND.comicVolume, "series", 1, {
      [ENTITY_KIND.comicInstallment]: ["chapter-2", "chapter-1"],
    });
    const volumeTwo = detail("volume-2", ENTITY_KIND.comicVolume, "series", 2, {
      [ENTITY_KIND.comicInstallment]: ["chapter-3"],
    });
    const selected = sequenceItem("chapter-1", "volume-1", 0);
    const entities = new Map([
      ["series", root],
      ["volume-1", volumeOne],
      ["volume-2", volumeTwo],
    ]);
    const thumbnails = new Map([
      ["chapter-1", thumbnail("chapter-1", "Chapter 1", 0, "volume-1")],
      ["chapter-2", thumbnail("chapter-2", "Chapter 2", 1, "volume-1")],
      ["chapter-3", thumbnail("chapter-3", "Chapter 3", 0, "volume-2")],
    ]);
    mocks.fetchEntity.mockImplementation(async (id: string) => entities.get(id));
    mocks.fetchOrderedEntityThumbnails.mockImplementation(async (ids: string[]) =>
      ids.flatMap((id) => thumbnails.get(id) ?? []));

    const result = await resolveOrderedEntitySequence(selected);

    expect(result?.items.map((item) => item.id)).toEqual([
      "chapter-1",
      "chapter-2",
      "chapter-3",
    ]);
    expect(result?.items.some((item) => item.id === "direct-1")).toBe(false);
  });
});

function sequenceItem(
  id: string,
  parentEntityId: string,
  sortOrder: number,
): EntityCardFull {
  return {
    ...detail(id, ENTITY_KIND.comicInstallment, parentEntityId, sortOrder, {}),
    capabilities: [{
      kind: CAPABILITY_KIND.orderedSequence,
      role: ENTITY_SEQUENCE_ROLE.item,
      itemKind: ENTITY_KIND.comicInstallment,
      containerKinds: [ENTITY_KIND.comicSeries, ENTITY_KIND.comicVolume],
    }],
  } as unknown as EntityCardFull;
}

function detail(
  id: string,
  kind: EntityCardFull["kind"],
  parentEntityId: string | null,
  sortOrder: number,
  children: Record<string, string[]>,
): EntityCardFull {
  return {
    id,
    kind,
    title: id,
    parentEntityId,
    sortOrder,
    capabilities: [],
    childrenByKind: Object.entries(children).map(([childKind, ids]) => ({
      kind: childKind,
      label: childKind,
      entities: ids.map((childId) => ({ id: childId })),
    })),
    relationships: [],
  } as unknown as EntityCardFull;
}

function thumbnail(
  id: string,
  title: string,
  sortOrder: number,
  parentEntityId: string,
): EntityThumbnail {
  return {
    id,
    kind: ENTITY_KIND.comicInstallment,
    title,
    parentEntityId,
    sortOrder,
  } as EntityThumbnail;
}
