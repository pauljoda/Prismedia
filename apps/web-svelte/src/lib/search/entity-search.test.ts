import { describe, expect, it } from "vitest";
import type { EntityKind } from "$lib/api/generated/model";
import type { EntityCard, EntityListResponse } from "$lib/api/entities";
import { firstSearchResult, flattenSearchResults, searchEntities } from "./entity-search";

const emptyPage: EntityListResponse = { items: [], nextCursor: null, totalCount: 0 };

describe("searchEntities", () => {
  it("returns direct matches before incoming relationship matches", async () => {
    const tim = entity("person-1", "person", "Tim Robinson");
    const sketch = entity("video-1", "video", "Hot Dog Car");
    const series = entity("series-1", "video-series", "I Think You Should Leave");
    const calls: unknown[] = [];

    const response = await searchEntities({
      query: "Tim Robinson",
      fetcher: async (params) => {
        calls.push(params);
        if (params?.query === "Tim Robinson") return page([tim]);
        if (params?.referencedBy === "person-1") return page([sketch, series]);
        return emptyPage;
      },
    });

    expect(flattenSearchResults(response).map((item) => item.title)).toEqual([
      "Tim Robinson",
      "Hot Dog Car",
      "I Think You Should Leave",
    ]);
    expect(flattenSearchResults(response).map((item) => item.matchType)).toEqual([
      "direct",
      "related",
      "related",
    ]);
    expect(firstSearchResult(response)?.href).toBe("/people/person-1");
    expect(calls).toContainEqual(expect.objectContaining({ referencedBy: "person-1" }));
  });

  it("filters output kinds without hiding related matches from unselected source kinds", async () => {
    const tim = entity("person-1", "person", "Tim Robinson");
    const sketch = entity("video-1", "video", "Hot Dog Car");

    const response = await searchEntities({
      query: "Tim",
      kinds: ["video"],
      fetcher: async (params) => {
        if (params?.query === "Tim") return page([tim]);
        if (params?.referencedBy === "person-1") return page([sketch]);
        return emptyPage;
      },
    });

    expect(response.groups).toHaveLength(1);
    expect(response.groups[0].kind).toBe("video");
    expect(response.groups[0].items[0]).toMatchObject({
      title: "Hot Dog Car",
      relatedTo: { title: "Tim Robinson" },
    });
  });

  it("deduplicates entities that are also direct matches", async () => {
    const tim = entity("person-1", "person", "Tim Robinson");
    const sketch = entity("video-1", "video", "Tim's Sketch");

    const response = await searchEntities({
      query: "Tim",
      fetcher: async (params) => {
        if (params?.query === "Tim") return page([tim, sketch]);
        if (params?.referencedBy === "person-1") return page([sketch]);
        return emptyPage;
      },
    });

    expect(flattenSearchResults(response).map((item) => item.id)).toEqual(["person-1", "video-1"]);
  });
});

function entity(id: string, kind: EntityKind, title: string): EntityCard {
  return {
    id,
    kind,
    title,
    parentEntityId: null,
    sortOrder: null,
    coverUrl: null,
    coverThumbUrl: null,
    hoverKind: "none",
    hoverUrl: null,
    hoverImages: [],
    meta: [],
    rating: null,
    isFavorite: false,
    isNsfw: false,
    isOrganized: true,
  };
}

function page(items: EntityCard[]): EntityListResponse {
  return { items, nextCursor: null, totalCount: items.length };
}
