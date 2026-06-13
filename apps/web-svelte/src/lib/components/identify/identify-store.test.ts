import { beforeEach, describe, expect, it, vi } from "vitest";
import { IdentifyStore } from "./identify-store.svelte";
import type { EntityMetadataProposal, PluginProvider } from "$lib/api/identify-types";
import type { EntityCard, EntityDetailCard } from "$lib/api/entities";
import { MAIN_SCROLL_TOP_EVENT } from "$lib/stores/main-scroll";

const fetchPluginProviders = vi.fn();
const fetchIdentifyQueue = vi.fn();
const fetchIdentifyEntity = vi.fn();
const fetchIdentifyQueueItem = vi.fn();
const addIdentifyQueueItem = vi.fn();
const requestIdentifySearch = vi.fn();
const applyIdentifyQueueItem = vi.fn();
const deleteIdentifyQueueItem = vi.fn();
const fetchIdentifyApplyProgress = vi.fn();
const identifyEntityTransient = vi.fn();
const saveIdentifyQueueProposal = vi.fn();
const startBulkIdentify = vi.fn();

vi.mock("$lib/api/plugins", async (importOriginal) => {
  const actual = await importOriginal<typeof import("$lib/api/plugins")>();
  return {
    ...actual,
    fetchPluginProviders: (...args: unknown[]) => fetchPluginProviders(...args),
  };
});

vi.mock("$lib/api/identify-client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("$lib/api/identify-client")>();
  return {
    ...actual,
    fetchIdentifyQueue: (...args: unknown[]) => fetchIdentifyQueue(...args),
    fetchIdentifyEntity: (...args: unknown[]) => fetchIdentifyEntity(...args),
    fetchIdentifyQueueItem: (...args: unknown[]) => fetchIdentifyQueueItem(...args),
    addIdentifyQueueItem: (...args: unknown[]) => addIdentifyQueueItem(...args),
    requestIdentifySearch: (...args: unknown[]) => requestIdentifySearch(...args),
    applyIdentifyQueueItem: (...args: unknown[]) => applyIdentifyQueueItem(...args),
    deleteIdentifyQueueItem: (...args: unknown[]) => deleteIdentifyQueueItem(...args),
    fetchIdentifyApplyProgress: (...args: unknown[]) => fetchIdentifyApplyProgress(...args),
    identifyEntityTransient: (...args: unknown[]) => identifyEntityTransient(...args),
    saveIdentifyQueueProposal: (...args: unknown[]) => saveIdentifyQueueProposal(...args),
    startBulkIdentify: (...args: unknown[]) => startBulkIdentify(...args),
  };
});

describe("IdentifyStore", () => {
  beforeEach(() => {
    fetchPluginProviders.mockReset();
    fetchIdentifyQueue.mockReset();
    fetchIdentifyEntity.mockReset();
    fetchIdentifyQueueItem.mockReset();
    addIdentifyQueueItem.mockReset();
    requestIdentifySearch.mockReset();
    applyIdentifyQueueItem.mockReset();
    deleteIdentifyQueueItem.mockReset();
    fetchIdentifyApplyProgress.mockReset();
    fetchPluginProviders.mockResolvedValue([]);
    fetchIdentifyQueue.mockResolvedValue([]);
    fetchIdentifyEntity.mockResolvedValue(null);
    fetchIdentifyQueueItem.mockResolvedValue(queueItem("video-1"));
    addIdentifyQueueItem.mockResolvedValue(queueItem("video-1"));
    requestIdentifySearch.mockResolvedValue(queueItem("video-1", { state: "queued" }));
    applyIdentifyQueueItem.mockResolvedValue(queueItem("video-1", { state: "done" }));
    deleteIdentifyQueueItem.mockResolvedValue(queueItem("video-1", { state: "deleted" }));
    identifyEntityTransient.mockReset();
    saveIdentifyQueueProposal.mockReset();
    startBulkIdentify.mockReset();
    // Keep child lookups pending so children stay in their initial loading/queued state for the
    // duration of a test rather than resolving and mutating the proposal mid-assertion.
    identifyEntityTransient.mockReturnValue(new Promise(() => {}));
    saveIdentifyQueueProposal.mockResolvedValue(queueItem("video-1"));
    startBulkIdentify.mockResolvedValue({
      job: {
        id: "bulk-job-1",
        type: "bulk-identify",
        status: "queued",
        progress: 0,
        message: "Queued",
        targetKind: null,
        targetId: null,
        targetLabel: "Bulk identify test",
        createdAt: "2026-05-25T00:00:00Z",
        startedAt: null,
        finishedAt: null,
      },
    });
    fetchIdentifyApplyProgress.mockResolvedValue({
      id: "apply-1",
      entityId: "video-1",
      state: "running",
      currentIndex: 1,
      total: 1,
      currentKind: "video",
      currentTitle: "Queued Movie",
      currentPath: ["Queued Movie"],
      error: null,
      updatedAt: "2026-05-25T00:00:00Z",
    });
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
        cascadeRunning: false,
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

  it("requests a search and upserts the returned queued item", async () => {
    const store = new IdentifyStore();
    const movie = entity("video-1", { kind: "video", title: "Friendship" });
    fetchIdentifyEntity.mockResolvedValue(detail("video-1", { kind: "video", title: "Friendship" }));
    requestIdentifySearch.mockResolvedValue(queueItem("video-1", { state: "queued", provider: "tmdb" }));

    const queued = await store.identifyEntity(movie, "tmdb");

    expect(requestIdentifySearch).toHaveBeenCalledWith("video-1", "tmdb", undefined, false);
    expect(queued?.state).toBe("queued");
    expect(store.queue.find((item) => item.entityId === "video-1")?.state).toBe("queued");
  });

  it("describes an item's in-flight search from its server state", () => {
    const store = new IdentifyStore();
    store.providers = [provider("tmdb", "The Movie Database")];
    store.queue = [
      { ...queueItem("video-1", { state: "queued" }), entity: entity("video-1") },
      { ...queueItem("video-2", { state: "searching", provider: "tmdb" }), entity: entity("video-2") },
      { ...queueItem("video-3", { state: "search" }), entity: entity("video-3") },
    ];

    expect(store.itemSearchStatus("video-1")).toBe("Queued for search");
    expect(store.itemSearchStatus("video-2")).toBe("Searching with The Movie Database");
    expect(store.itemSearchStatus("video-3")).toBeNull();
    expect(store.isItemBusy("video-1")).toBe(true);
    expect(store.isItemBusy("video-2")).toBe(true);
    expect(store.isItemBusy("video-3")).toBe(false);
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

    const queued = await store.seedEntity("video-1", "video-1");

    expect(fetchIdentifyQueueItem).toHaveBeenCalledWith("video-1");
    expect(addIdentifyQueueItem).not.toHaveBeenCalled();
    expect(requestIdentifySearch).not.toHaveBeenCalled();
    expect(queued?.state).toBe("proposal");
    expect(store.view.kind).toBe("review-parent");
  });

  it("opening an item never triggers a search regardless of its state", async () => {
    const store = new IdentifyStore();
    fetchPluginProviders.mockResolvedValue([provider("tmdb", "The Movie Database")]);
    fetchIdentifyQueueItem.mockResolvedValue(queueItem("video-1", { state: "search", provider: "tmdb" }));
    fetchIdentifyEntity.mockResolvedValue(detail("video-1", { kind: "video", title: "Friendship" }));

    const queued = await store.seedEntity("video-1", null);

    expect(requestIdentifySearch).not.toHaveBeenCalled();
    expect(queued?.provider).toBe("tmdb");
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
    requestIdentifySearch.mockResolvedValue(queueItem("video-1", { state: "queued", provider: "tmdb" }));

    await store.backToSearch(movie, "tmdb");

    expect(requestIdentifySearch).toHaveBeenCalledWith("video-1", "tmdb", {
      title: "Friendship",
      requireChoice: true,
    }, false);
    expect(store.queue.find((item) => item.entityId === "video-1")?.state).toBe("queued");
  });

  it("polls the queue item while a cascade streams children and stops when it completes", async () => {
    vi.useFakeTimers();
    try {
      const store = new IdentifyStore();
      const artist = entity("artist-1", { kind: "music-artist", title: "Imagine Dragons" });
      const seed = proposal("musicbrainz:artist:1", { targetKind: "music-artist", title: "Imagine Dragons" });
      store.queue = [{
        ...queueItem("artist-1", { state: "proposal", provider: "musicbrainz", proposal: seed, cascadeRunning: true }),
        entity: artist,
        detail: detail("artist-1", { kind: "music-artist", title: "Imagine Dragons" }),
      }];

      // The cascade has finished server-side: the polled item carries a child and is no longer running.
      const child = proposal("musicbrainz:release:9", { targetKind: "audio-library", title: "Evolve" });
      const completed = {
        ...queueItem("artist-1", {
          state: "proposal",
          provider: "musicbrainz",
          proposal: { ...seed, children: [{ ...child, targetEntityId: "album-1" }] },
          cascadeRunning: false,
        }),
      };
      fetchIdentifyQueue.mockResolvedValue([completed]);

      store.ensureQueuePolling();
      await vi.advanceTimersByTimeAsync(400);

      const item = store.queue.find((q) => q.entityId === "artist-1");
      expect(item?.cascadeRunning).toBe(false);
      expect(item?.proposal?.children).toHaveLength(1);
      expect(store.cascadeRunning("artist-1")).toBe(false);
    } finally {
      vi.useRealTimers();
    }
  });

  it("keeps apply progress visible briefly before navigating away", async () => {
    vi.useFakeTimers();
    try {
      const store = new IdentifyStore();
      const movie = entity("video-1", { kind: "video", title: "Friendship" });
      const accepted = proposal("tmdb:movie:123", { targetKind: "video", title: "Friendship" });

      const apply = store.applyProposal(movie, accepted, ["title"]);

      expect(store.applying).toBe(true);
      expect(store.applyProgress?.currentPath).toEqual(["Friendship"]);
      await vi.advanceTimersByTimeAsync(300);
      expect(store.applying).toBe(true);
      await vi.advanceTimersByTimeAsync(400);
      await apply;

      expect(applyIdentifyQueueItem).toHaveBeenCalledWith("video-1", accepted, ["title"], undefined, {
        progressId: expect.any(String),
      });
      expect(store.applying).toBe(false);
      expect(store.applyProgress).toBeNull();
      expect(store.view.kind).toBe("dashboard");
    } finally {
      vi.useRealTimers();
    }
  });

  it("starts a bulk batch with one request and shows the queued rows immediately", async () => {
    const store = new IdentifyStore();
    const first = entity("video-1", { kind: "video", title: "First" });
    const second = entity("video-2", { kind: "video", title: "Second" });
    startBulkIdentify.mockResolvedValue({ requested: 2, enqueued: 2 });
    fetchIdentifyQueue.mockResolvedValue([
      queueItem("video-1", { state: "queued", provider: "tmdb" }),
      queueItem("video-2", { state: "queued", provider: "tmdb" }),
    ]);

    await store.startBulk("tmdb", [first, second]);

    // One POST creates every row and per-entity search job server-side; no per-item
    // add or client-side search runs, and the user lands on the dashboard.
    expect(startBulkIdentify).toHaveBeenCalledWith("tmdb", ["video-1", "video-2"], null, false);
    expect(addIdentifyQueueItem).not.toHaveBeenCalled();
    expect(requestIdentifySearch).not.toHaveBeenCalled();
    expect(store.view.kind).toBe("dashboard");
    expect(store.queue.map((item) => item.entityId)).toEqual(["video-1", "video-2"]);
    expect(store.queue.every((item) => item.state === "queued")).toBe(true);
    store.destroy();
  });

  it("polls the queue while searches are in flight and stops when everything settles", async () => {
    vi.useFakeTimers();
    try {
      const store = new IdentifyStore();
      store.queue = [{
        ...queueItem("video-1", { state: "searching", provider: "tmdb" }),
        entity: entity("video-1", { kind: "video", title: "First" }),
      }];
      fetchIdentifyQueue.mockResolvedValue([
        queueItem("video-1", {
          state: "proposal",
          provider: "tmdb",
          proposal: proposal("tmdb:video-1", { targetKind: "video", title: "First" }),
        }),
      ]);

      store.ensureQueuePolling();
      await vi.advanceTimersByTimeAsync(400);

      expect(fetchIdentifyQueue).toHaveBeenCalledWith(false, false, { signal: expect.any(AbortSignal) });
      expect(store.queue[0]?.state).toBe("proposal");

      // Nothing is live anymore, so the loop stops itself: no further fetches fire.
      const calls = fetchIdentifyQueue.mock.calls.length;
      await vi.advanceTimersByTimeAsync(3000);
      expect(fetchIdentifyQueue.mock.calls.length).toBe(calls);
    } finally {
      vi.useRealTimers();
    }
  });

  it("waits for a specific provider search to settle and merges the result", async () => {
    vi.useFakeTimers();
    try {
      const store = new IdentifyStore();
      store.queue = [{
        ...queueItem("video-1", { state: "queued", provider: "tmdb" }),
        entity: entity("video-1", { kind: "video", title: "Friendship" }),
      }];
      fetchIdentifyQueue
        .mockResolvedValueOnce([
          queueItem("video-1", { state: "searching", provider: "tmdb" }),
        ])
        .mockResolvedValueOnce([
          queueItem("video-1", {
            state: "proposal",
            provider: "tmdb",
            proposal: proposal("tmdb:video-1", { targetKind: "video", title: "Friendship" }),
          }),
        ]);

      const result = store.waitForIdentifyResult("video-1", "tmdb", { pollMs: 20, timeoutMs: 1_000 });
      await vi.advanceTimersByTimeAsync(20);
      await vi.advanceTimersByTimeAsync(20);

      await expect(result).resolves.toMatchObject({ state: "proposal", provider: "tmdb" });
      expect(store.queue[0]?.state).toBe("proposal");
    } finally {
      vi.useRealTimers();
    }
  });

  it("accepts selected queued proposals and clears them from the queue", async () => {
    const store = new IdentifyStore();
    const ready = {
      ...queueItem("video-1", {
        state: "proposal",
        provider: "tmdb",
        proposal: proposal("tmdb:movie:123", { targetKind: "video", title: "Friendship" }),
      }),
      entity: entity("video-1", { kind: "video", title: "Friendship" }),
      detail: detail("video-1", { kind: "video", title: "Friendship" }),
    };
    const pending = {
      ...queueItem("video-2", { state: "search", provider: "tmdb" }),
      entity: entity("video-2", { kind: "video", title: "Pending" }),
      detail: null,
    };
    store.queue = [ready, pending];
    applyIdentifyQueueItem.mockResolvedValue(queueItem("video-1", { state: "done" }));

    await store.acceptQueueProposals([ready, pending]);

    expect(applyIdentifyQueueItem).toHaveBeenCalledTimes(1);
    expect(applyIdentifyQueueItem.mock.calls[0][0]).toBe("video-1");
    expect(store.queue.map((item) => item.entityId)).toEqual(["video-2"]);
    expect(store.bulkAccepting).toBe(false);
  });

  it("rejects a queued item and advances to the next reviewable queue item", async () => {
    const store = new IdentifyStore();
    const first = {
      ...queueItem("video-1", {
        state: "proposal",
        provider: "tmdb",
        proposal: proposal("tmdb:movie:123", { targetKind: "video", title: "Friendship" }),
      }),
      entity: entity("video-1", { kind: "video", title: "Friendship" }),
      detail: detail("video-1", { kind: "video", title: "Friendship" }),
    };
    const second = {
      ...queueItem("video-2", { state: "search", provider: "tmdb" }),
      entity: entity("video-2", { kind: "video", title: "Pending" }),
      detail: null,
    };
    store.queue = [first, second];
    const reviewQueueItem = vi.spyOn(store, "reviewQueueItem");
    deleteIdentifyQueueItem.mockResolvedValue(queueItem("video-1", { state: "deleted" }));

    await store.rejectQueueItem("video-1", { navigateNext: true });

    expect(deleteIdentifyQueueItem).toHaveBeenCalledWith("video-1");
    expect(store.queue.map((item) => item.entityId)).toEqual(["video-2"]);
    expect(reviewQueueItem).toHaveBeenCalledWith(expect.objectContaining({ entityId: "video-2" }));
  });
});

function queueItem(
  id: string,
  options: {
    isNsfw?: boolean;
    state?: "search" | "queued" | "searching" | "proposal" | "done" | "deleted" | "error";
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
    cascadeRunning?: boolean;
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
    cascadeRunning: options.cascadeRunning ?? false,
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
    coverThumbUrl: null,
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
  options: { targetKind?: EntityMetadataProposal["targetKind"]; title?: string } = {},
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

function provider(id: string, name: string, entityKind = "video"): PluginProvider {
  return {
    id,
    name,
    version: "1.0.0",
    installed: true,
    enabled: true,
    isNsfw: false,
    supports: [{ entityKind, actions: ["search"] }],
    auth: [],
    missingAuthKeys: [],
  };
}
