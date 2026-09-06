import { describe, expect, it, vi } from "vitest";
import { ENTITY_KIND, THUMBNAIL_HOVER_KIND } from "$lib/api/generated/codes";
import type { EntityCard, EntityListResponse } from "$lib/api/entities";
import type { ListEntitiesParams } from "$lib/api/generated/model";
import { flattenSearchResults, loadMoreSearchResults, searchEntities } from "./entity-search";

function entity(id: string, kind: EntityCard["kind"] = ENTITY_KIND.movie): EntityCard {
  return { id, kind, title: id, parentEntityId: null, sortOrder: null, coverUrl: null, coverThumbUrl: null, hoverKind: THUMBNAIL_HOVER_KIND.none, hoverUrl: null, hoverImages: [], meta: [], rating: null, isFavorite: false, isNsfw: false, isOrganized: true };
}
function page(items: EntityCard[], nextCursor: string | null = null): EntityListResponse {
  return { items, nextCursor, totalCount: items.length };
}

describe("search continuation", () => {
  it("loads direct pages only when asked and preserves query and safety filters", async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(page([entity("first")], "next")).mockResolvedValueOnce(page([entity("second")]));
    const initial = await searchEntities({ query: "title", hideNsfw: true, directLimit: 1, fetcher });
    expect(fetcher).toHaveBeenCalledOnce();
    expect(initial.continuation?.requests).toHaveLength(1);
    const next = await loadMoreSearchResults(initial, fetcher);
    expect(fetcher).toHaveBeenLastCalledWith({ query: "title", hideNsfw: true, limit: 1, cursor: "next" });
    expect(flattenSearchResults(next).map(item => item.id)).toEqual(["first", "second"]);
    expect(next.continuation).toBeUndefined();
  });

  it("retains related cursors and defers sources beyond the initial request budget", async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(page([entity("person-a", ENTITY_KIND.person), entity("person-b", ENTITY_KIND.person)]))
      .mockResolvedValueOnce(page([entity("film-a")], "related-next"));
    const initial = await searchEntities({ query: "people", hideNsfw: true, relatedSourceLimit: 1, relatedLimitPerSource: 2, fetcher });
    expect(fetcher).toHaveBeenCalledTimes(2);
    expect(initial.continuation?.requests).toEqual(expect.arrayContaining([
      expect.objectContaining({ params: { referencedBy: "person-a", hideNsfw: true, limit: 2, cursor: "related-next" } }),
      expect.objectContaining({ params: { referencedBy: "person-b", hideNsfw: true, limit: 2 } }),
    ]));
  });

  it("keeps successful results and failed requests available for retry", async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(page([entity("first")], "next"));
    const initial = await searchEntities({ query: "title", fetcher });
    fetcher.mockRejectedValueOnce(new Error("Offline"));
    const failed = await loadMoreSearchResults(initial, fetcher);
    expect(flattenSearchResults(failed).map(item => item.id)).toEqual(["first"]);
    expect(failed.partialFailure).toBe(true);
    expect(failed.continuation?.requests[0].params).toEqual(initial.continuation?.requests[0].params);
    fetcher.mockResolvedValueOnce(page([entity("second")]));
    const recovered = await loadMoreSearchResults(failed, fetcher);
    expect(recovered.partialFailure).toBe(false);
    expect(flattenSearchResults(recovered)).toHaveLength(2);
  });

  it("deduplicates page boundaries and promotes a later direct match over a related one", async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(page([entity("person", ENTITY_KIND.person)], "next"))
      .mockResolvedValueOnce(page([entity("film")]))
      .mockResolvedValueOnce(page([entity("film"), entity("person", ENTITY_KIND.person)]));
    const initial = await searchEntities({ query: "title", fetcher });
    const next = await loadMoreSearchResults(initial, fetcher);
    expect(flattenSearchResults(next)).toHaveLength(2);
    expect(flattenSearchResults(next).find(item => item.id === "film")?.matchType).toBe("direct");
    expect(fetcher).toHaveBeenCalledTimes(3);
  });

  it("does not publish a looping server cursor as progress", async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(page([entity("first")], "same"));
    const initial = await searchEntities({ query: "title", fetcher });
    fetcher.mockResolvedValueOnce(page([entity("first")], "same"));
    const next = await loadMoreSearchResults(initial, fetcher);
    expect(next.partialFailure).toBe(true);
    expect(flattenSearchResults(next)).toHaveLength(1);
  });

  it("eventually visits every deferred source without exceeding the per-click request budget", async () => {
    const fetcher = vi.fn(async (params?: ListEntitiesParams) => params?.query
      ? page([entity("person-a", ENTITY_KIND.person), entity("person-b", ENTITY_KIND.person), entity("person-c", ENTITY_KIND.person)])
      : page([entity(`film-${params?.referencedBy}`)]));
    let result = await searchEntities({ query: "people", relatedSourceLimit: 1, fetcher });
    expect(fetcher).toHaveBeenCalledTimes(2);
    for (let i = 0; i < 2; i += 1) {
      const before = fetcher.mock.calls.length;
      result = await loadMoreSearchResults(result, fetcher);
      expect(fetcher.mock.calls.length - before).toBe(1);
    }
    expect(result.continuation).toBeUndefined();
    expect(flattenSearchResults(result)).toHaveLength(6);
  });
});
