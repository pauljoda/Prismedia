import { describe, expect, it } from "vitest";
import { resolveEntityHrefById, type EntityRouteRecord } from "./entity-route-resolver";

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
  it("resolves top-level entity routes directly", async () => {
    await expect(resolveEntityHrefById("book", fetchRecord)).resolves.toBe("/books/book");
  });

  it("resolves child entity routes by walking to the required parent", async () => {
    await expect(resolveEntityHrefById("volume", fetchRecord)).resolves.toBe("/books/book/volumes/volume");
    await expect(resolveEntityHrefById("chapter", fetchRecord)).resolves.toBe("/books/book/chapters/chapter");
    await expect(resolveEntityHrefById("season", fetchRecord)).resolves.toBe("/series/series/seasons/season");
  });

});
