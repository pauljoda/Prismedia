import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { EntityKind } from "$lib/api/generated/model";
import type { EntityCard } from "$lib/api/entities";
import { EntityIndexPageState } from "./entity-index-page.svelte.ts";

const fetchEntities = vi.fn();

vi.mock("$lib/api/entities", async (importOriginal) => {
  const actual = await importOriginal<typeof import("$lib/api/entities")>();
  return {
    ...actual,
    fetchEntities: (...args: unknown[]) => fetchEntities(...args),
  };
});

describe("EntityIndexPageState", () => {
  beforeEach(() => {
    fetchEntities.mockReset();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("loads initial and cursor pages into thumbnail cards with browse links", async () => {
    fetchEntities
      .mockResolvedValueOnce({
        items: [entity("video-1", "video", "First Video")],
        nextCursor: "next-page",
        totalCount: 2,
      })
      .mockResolvedValueOnce({
        items: [entity("video-2", "video", "Second Video")],
        nextCursor: null,
        totalCount: 2,
      });

    const state = new EntityIndexPageState({
      getKind: () => "video",
      getHideNsfw: () => true,
    });

    await state.loadInitial();

    expect(fetchEntities).toHaveBeenCalledWith(
      { kind: "video", query: undefined, hideNsfw: true, limit: 250 },
      { signal: expect.any(AbortSignal) },
    );
    expect(state.loadState).toBe("ready");
    expect(state.cards.map((card) => card.href)).toEqual(["/videos/video-1"]);
    expect(state.nextCursor).toBe("next-page");
    expect(state.totalCount).toBe(2);

    await state.loadMore();

    expect(fetchEntities).toHaveBeenLastCalledWith({
      kind: "video",
      query: undefined,
      cursor: "next-page",
      hideNsfw: true,
      limit: 250,
    });
    expect(state.cards.map((card) => card.entity.title)).toEqual(["First Video", "Second Video"]);
    expect(state.nextCursor).toBeNull();
  });

  it("routes movie child videos to the movie detail surface", async () => {
    fetchEntities.mockResolvedValueOnce({
      items: [
        entity("video-1", "video", "Friendship", {
          parentEntityId: "movie-1",
          parentKind: "movie",
        }),
      ],
      nextCursor: null,
      totalCount: 1,
    });

    const state = new EntityIndexPageState({
      getKind: () => "video",
      getHideNsfw: () => false,
    });

    await state.loadInitial();

    expect(state.cards.map((card) => card.href)).toEqual(["/movies/movie-1"]);
  });

  it("reloads the first server page when the grid page size changes", async () => {
    fetchEntities
      .mockResolvedValueOnce({
        items: [entity("video-1", "video", "First Video")],
        nextCursor: "next-page",
        totalCount: 2,
      })
      .mockResolvedValueOnce({
        items: [entity("video-2", "video", "Second Video")],
        nextCursor: null,
        totalCount: 1,
      });

    const state = new EntityIndexPageState({
      getKind: () => "video",
      getHideNsfw: () => false,
    });

    await state.loadInitial();
    state.setPageSize(500);
    await Promise.resolve();
    await Promise.resolve();

    expect(fetchEntities).toHaveBeenCalledTimes(2);
    expect(fetchEntities).toHaveBeenLastCalledWith(
      { kind: "video", query: undefined, hideNsfw: false, limit: 500 },
      { signal: expect.any(AbortSignal) },
    );
    expect(state.cards.map((card) => card.entity.title)).toEqual(["Second Video"]);
    expect(state.totalCount).toBe(1);
  });

  it("debounces query changes and reloads with the trimmed query", async () => {
    vi.useFakeTimers();
    fetchEntities
      .mockResolvedValueOnce({
        items: [entity("video-1", "video", "First Video")],
        nextCursor: null,
        totalCount: 1,
      })
      .mockResolvedValueOnce({
        items: [entity("video-2", "video", "Second Video")],
        nextCursor: null,
        totalCount: 1,
      });

    const state = new EntityIndexPageState({
      getKind: () => "video",
      getHideNsfw: () => false,
    });

    await state.loadInitial();
    state.setQuery("  bunny  ");

    expect(fetchEntities).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(300);

    expect(fetchEntities).toHaveBeenCalledTimes(2);
    expect(fetchEntities).toHaveBeenLastCalledWith(
      { kind: "video", query: "bunny", hideNsfw: false, limit: 250 },
      { signal: expect.any(AbortSignal) },
    );
    expect(state.query).toBe("bunny");
    expect(state.totalCount).toBe(1);
  });
});

function entity(id: string, kind: EntityKind, title: string, overrides: Partial<EntityCard> = {}): EntityCard {
  return {
    id,
    kind,
    title,
    parentEntityId: null,
    parentKind: null,
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
    isOrganized: false,
    ...overrides,
  };
}
