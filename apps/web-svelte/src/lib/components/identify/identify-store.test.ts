import { beforeEach, describe, expect, it, vi } from "vitest";
import { IdentifyStore } from "./identify-store.svelte";
import type { EntityMetadataProposal } from "$lib/api/identify";
import type { EntityCard, EntityDetailCard } from "$lib/api/entities";
import { MAIN_SCROLL_TOP_EVENT } from "$lib/stores/main-scroll";

const fetchPluginProviders = vi.fn();
const fetchIdentifyQueue = vi.fn();
const fetchIdentifyEntity = vi.fn();
const fetchIdentifyQueueItem = vi.fn();
const addIdentifyQueueItem = vi.fn();
const searchIdentifyQueueItem = vi.fn();

vi.mock("$lib/api/identify", async (importOriginal) => {
  const actual = await importOriginal<typeof import("$lib/api/identify")>();
  return {
    ...actual,
    fetchPluginProviders: (...args: unknown[]) => fetchPluginProviders(...args),
    fetchIdentifyQueue: (...args: unknown[]) => fetchIdentifyQueue(...args),
    fetchIdentifyEntity: (...args: unknown[]) => fetchIdentifyEntity(...args),
    fetchIdentifyQueueItem: (...args: unknown[]) => fetchIdentifyQueueItem(...args),
    addIdentifyQueueItem: (...args: unknown[]) => addIdentifyQueueItem(...args),
    searchIdentifyQueueItem: (...args: unknown[]) => searchIdentifyQueueItem(...args),
  };
});

describe("IdentifyStore", () => {
  beforeEach(() => {
    fetchPluginProviders.mockReset();
    fetchIdentifyQueue.mockReset();
    fetchIdentifyEntity.mockReset();
    fetchIdentifyQueueItem.mockReset();
    addIdentifyQueueItem.mockReset();
    searchIdentifyQueueItem.mockReset();
    fetchPluginProviders.mockResolvedValue([]);
    fetchIdentifyQueue.mockResolvedValue([]);
    fetchIdentifyEntity.mockResolvedValue(null);
    fetchIdentifyQueueItem.mockResolvedValue(queueItem("video-1"));
    addIdentifyQueueItem.mockResolvedValue(queueItem("video-1"));
    searchIdentifyQueueItem.mockResolvedValue(queueItem("video-1", { state: "search" }));
  });

  it("resets stale review state when entering the dashboard route", async () => {
    const store = new IdentifyStore();
    store.view = {
      kind: "review-parent",
      entity: entity("series-1"),
      proposal: proposal("proposal-1"),
      detail: null,
    };

    await store.enterDashboardRoute();

    expect(store.view.kind).toBe("dashboard");
    expect(fetchPluginProviders).toHaveBeenCalledOnce();
    expect(fetchIdentifyQueue).toHaveBeenCalledOnce();
  });

  it("passes current NSFW visibility when loading and refreshing the queue", async () => {
    const store = new IdentifyStore(() => true);

    await store.loadInitial();

    expect(fetchIdentifyQueue).toHaveBeenCalledWith(false, true);

    fetchIdentifyQueue.mockClear();
    fetchIdentifyQueue.mockResolvedValue([queueItem("nsfw-1", { isNsfw: true })]);

    await store.syncNsfwVisibility(false);

    expect(fetchIdentifyQueue).toHaveBeenCalledWith(false, false);
    expect(store.queue[0]?.entity.isNsfw).toBe(true);
  });

  it("does not refetch the queue when NSFW visibility is unchanged", async () => {
    const store = new IdentifyStore(() => false);

    await store.loadInitial();
    fetchIdentifyQueue.mockClear();

    await store.syncNsfwVisibility(false);

    expect(fetchIdentifyQueue).not.toHaveBeenCalled();
  });

  it("hydrates relationship proposal current detail from the scoped entity relationships", async () => {
    const store = new IdentifyStore();
    const creditProposal = proposal("tmdb:person:1500059", {
      targetKind: "person",
      title: "Tim Robinson",
    });
    const personDetail = detail("person-tim", {
      kind: "person",
      title: "Tim Robinson",
    });
    store.queue = [
      {
        id: "queue-1",
        entityId: "series-1",
        entityKind: "video-series",
        title: "Series",
        isNsfw: false,
        state: "proposal",
        action: "search",
        candidates: [],
        proposal: proposal("series-proposal"),
        entity: entity("series-1"),
        detail: detail("series-1", {
          kind: "video-series",
          title: "Series",
          relationships: [
            {
              kind: "person",
              label: "Credits",
              entities: [entity("person-tim", { kind: "person", title: "Tim Robinson" })],
            },
          ],
        }),
      },
    ];
    fetchIdentifyEntity.mockResolvedValue(personDetail);

    await store.ensureReviewDetailForProposal("series-1", creditProposal);

    expect(fetchIdentifyEntity).toHaveBeenCalledWith("person-tim");
    expect(store.getReviewDetailForProposal("series-1", creditProposal)?.id).toBe(personDetail.id);
    expect(store.getReviewDetailForProposal("series-1", creditProposal)?.title).toBe(personDetail.title);
  });

  it("requests a main scroll reset when navigating between review views", () => {
    const store = new IdentifyStore();
    const dispatchEvent = vi.spyOn(window, "dispatchEvent");

    store.navigateTo({
      kind: "review-child",
      entity: entity("series-1"),
      proposal: proposal("child-proposal", { targetKind: "video-episode", title: "Episode" }),
      parentProposal: proposal("parent-proposal"),
      ancestors: [proposal("parent-proposal")],
    });

    expect(dispatchEvent.mock.calls.some(([event]) => event.type === MAIN_SCROLL_TOP_EVENT)).toBe(true);
  });

  it("auto-identifies a queued entity with the selected provider", async () => {
    const store = new IdentifyStore();
    const movie = entity("video-1", { kind: "video", title: "Friendship" });
    addIdentifyQueueItem.mockResolvedValue(queueItem("video-1", { title: "Friendship" }));
    fetchIdentifyEntity.mockResolvedValue(detail("video-1", { kind: "video", title: "Friendship" }));
    searchIdentifyQueueItem.mockResolvedValue(queueItem("video-1", {
      state: "proposal",
      provider: "tmdb",
      proposal: proposal("tmdb:movie:123", { targetKind: "video", title: "Friendship" }),
    }));

    const queued = await store.queueEntity(movie, "tmdb");

    expect(addIdentifyQueueItem).toHaveBeenCalledWith("video-1");
    expect(searchIdentifyQueueItem).toHaveBeenCalledWith("video-1", "tmdb", undefined);
    expect(queued?.state).toBe("proposal");
    expect(store.view.kind).toBe("review-parent");
  });

  it("opens an existing queued item without enqueueing or searching again", async () => {
    const store = new IdentifyStore();
    fetchIdentifyQueueItem.mockResolvedValue(queueItem("video-1", {
      state: "proposal",
      provider: "tmdb",
      proposal: proposal("tmdb:movie:123", { targetKind: "video", title: "Friendship" }),
    }));
    fetchIdentifyEntity.mockResolvedValue(detail("video-1", { kind: "video", title: "Friendship" }));
    fetchIdentifyQueue.mockResolvedValue([]);

    const queued = await store.seedEntity("video-1", "video-1", { openExistingOnly: true });

    expect(fetchIdentifyQueueItem).toHaveBeenCalledWith("video-1");
    expect(addIdentifyQueueItem).not.toHaveBeenCalled();
    expect(searchIdentifyQueueItem).not.toHaveBeenCalled();
    expect(queued?.state).toBe("proposal");
    expect(store.view.kind).toBe("review-parent");
  });

  it("falls back to a title search when an exact identify attempt misses", async () => {
    const store = new IdentifyStore();
    const movie = entity("video-1", { kind: "video", title: "Friendship" });
    store.queue = [{
      ...queueItem("video-1"),
      entity: movie,
      detail: detail("video-1", { kind: "video", title: "Friendship" }),
    }];
    searchIdentifyQueueItem
      .mockResolvedValueOnce(queueItem("video-1", {
        state: "error",
        provider: "tmdb",
        error: "No exact match",
      }))
      .mockResolvedValueOnce(queueItem("video-1", {
        state: "search",
        provider: "tmdb",
        candidates: [
          {
            externalIds: { tmdb: "123" },
            title: "Friendship",
            year: 2025,
            overview: null,
            posterUrl: null,
            popularity: null,
          },
        ],
      }));

    const resolved = await store.identifyEntity(movie, "tmdb");

    expect(searchIdentifyQueueItem).toHaveBeenNthCalledWith(1, "video-1", "tmdb", undefined);
    expect(searchIdentifyQueueItem).toHaveBeenNthCalledWith(2, "video-1", "tmdb", { title: "Friendship" });
    expect(resolved?.state).toBe("search");
    expect(store.view.kind).toBe("review-choice");
  });

  it("requires a candidate pick when returning from a proposal to search", async () => {
    const store = new IdentifyStore();
    const movie = entity("video-1", { kind: "video", title: "Friendship" });
    store.queue = [{
      ...queueItem("video-1", {
        state: "proposal",
        provider: "tmdb",
        proposal: proposal("tmdb:movie:123", { targetKind: "video", title: "Friendship" }),
      }),
      entity: movie,
      detail: detail("video-1", { kind: "video", title: "Friendship" }),
    }];
    searchIdentifyQueueItem.mockResolvedValue(queueItem("video-1", {
      state: "search",
      provider: "tmdb",
      candidates: [
        {
          externalIds: { tmdb: "456" },
          title: "Friendship!",
          year: 2010,
          overview: null,
          posterUrl: null,
          popularity: null,
        },
      ],
    }));

    await store.backToSearch(movie, "tmdb");

    expect(searchIdentifyQueueItem).toHaveBeenCalledWith("video-1", "tmdb", {
      title: "Friendship",
      requireChoice: true,
    });
    expect(store.view.kind).toBe("review-choice");
  });
});

function queueItem(
  id: string,
  options: {
    isNsfw?: boolean;
    state?: "search" | "proposal" | "done" | "deleted" | "error";
    provider?: string | null;
    title?: string;
    candidates?: Array<{
      externalIds: Record<string, string>;
      title: string;
      year?: number | null;
      overview?: string | null;
      posterUrl?: string | null;
      popularity?: number | null;
    }>;
    proposal?: EntityMetadataProposal | null;
    error?: string | null;
  } = {},
) {
  return {
    id: `queue-${id}`,
    entityId: id,
    entityKind: "video",
    title: options.title ?? "Queued Movie",
    isNsfw: options.isNsfw ?? false,
    state: options.state ?? "search",
    provider: options.provider ?? null,
    action: "search",
    query: null,
    candidates: options.candidates ?? [],
    proposal: options.proposal ?? null,
    error: options.error ?? null,
    createdAt: "2026-05-25T00:00:00Z",
    updatedAt: "2026-05-25T00:00:00Z",
    completedAt: null,
  };
}

function entity(id: string, options: Partial<EntityCard> = {}): EntityCard {
  return {
    id,
    kind: options.kind ?? "video-series",
    title: options.title ?? "Series",
    parentEntityId: null,
    sortOrder: null,
    coverUrl: null,
    hoverKind: "none",
    hoverUrl: null,
    hoverImages: [],
    meta: [],
    rating: null,
    isFavorite: false,
    isNsfw: false,
    isOrganized: false,
    ...options,
  };
}

function detail(id: string, options: Partial<EntityDetailCard> = {}): EntityDetailCard {
  return {
    id,
    kind: options.kind ?? "video-series",
    title: options.title ?? "Series",
    parentEntityId: null,
    sortOrder: null,
    capabilities: [],
    childrenByKind: [],
    relationships: [],
    ...options,
  };
}

function proposal(
  proposalId: string,
  options: { targetKind?: string; title?: string } = {},
): EntityMetadataProposal {
  return {
    proposalId,
    provider: "tmdb",
    targetKind: options.targetKind ?? "video-series",
    confidence: 1,
    matchReason: "test",
    patch: {
      title: options.title ?? "Series",
      description: null,
      externalIds: {},
      urls: [],
      tags: [],
      studio: null,
      credits: [],
      dates: {},
      stats: {},
      positions: {},
      classification: null,
    },
    images: [],
    children: [],
    relationships: [],
    candidates: [],
    targetEntityId: null,
  };
}
