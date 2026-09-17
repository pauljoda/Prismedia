import { beforeEach, describe, expect, it, vi } from "vitest";
import { resolveEntityHrefById, type EntityRouteRecord } from "./entity-route-resolver";

const api = vi.hoisted(() => ({ fetchEntity: vi.fn() }));
vi.mock("$lib/api/entities", () => ({ fetchEntity: api.fetchEntity }));

const records: Record<string, EntityRouteRecord> = {
  book: { id: "book", kind: "book", parentEntityId: null },
  volume: { id: "volume", kind: "book-volume", parentEntityId: "book" },
  chapter: { id: "chapter", kind: "book-chapter", parentEntityId: "volume" },
  series: { id: "series", kind: "video-series", parentEntityId: null },
  season: { id: "season", kind: "video-season", parentEntityId: "series" },
};

async function fetchRecord(id: string): Promise<EntityRouteRecord> {
  const record = records[id];
  if (!record) throw new Error(`Missing ${id}`);
  return record;
}

describe("entity route resolver", () => {
  beforeEach(() => {
    api.fetchEntity.mockReset();
  });

  it("resolves top-level entity routes directly", async () => {
    await expect(resolveEntityHrefById("book", fetchRecord)).resolves.toBe("/books/book");
  });

  it("resolves child entity routes by walking to the required parent", async () => {
    await expect(resolveEntityHrefById("volume", fetchRecord)).resolves.toBe("/books/book/volumes/volume");
    await expect(resolveEntityHrefById("chapter", fetchRecord)).resolves.toBe("/books/book/chapters/chapter");
    await expect(resolveEntityHrefById("season", fetchRecord)).resolves.toBe("/series/series/seasons/season");
  });

  it("uses the explicit visibility choice for the entity and its route ancestors", async () => {
    api.fetchEntity.mockImplementation((id: string) => fetchRecord(id));

    await expect(resolveEntityHrefById("chapter", { hideNsfw: false })).resolves.toBe("/books/book/chapters/chapter");

    expect(api.fetchEntity).toHaveBeenNthCalledWith(1, "chapter", { hideNsfw: false });
    expect(api.fetchEntity).toHaveBeenNthCalledWith(2, "volume", { hideNsfw: false });
    expect(api.fetchEntity).toHaveBeenNthCalledWith(3, "book", { hideNsfw: false });
  });

});
