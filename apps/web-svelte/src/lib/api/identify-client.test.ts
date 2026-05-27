import { describe, expect, it, vi } from "vitest";
import {
  applyIdentifyQueueItem,
  fetchIdentifyQueue,
  fetchOptionalIdentifyQueueItem,
  searchIdentifyQueueItem,
} from "./identify-client";

describe("identify client", () => {
  it("passes NSFW visibility through the generated queue params", async () => {
    const fetchMock = mockFetch([]);

    await fetchIdentifyQueue(false, true);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/identify/queue?includeCompleted=false&hideNsfw=true",
      expect.objectContaining({ method: "GET" }),
    );
  });

  it("posts queue search requests through the generated endpoint", async () => {
    const fetchMock = mockFetch(queueItem("video-1"));

    await searchIdentifyQueueItem("video-1", "tmdb", { title: "Friendship" });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/identify/queue/entities/video-1/search",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ provider: "tmdb", query: { title: "Friendship" } }),
      }),
    );
  });

  it("applies reviewed queue proposals with selected fields and images", async () => {
    const fetchMock = mockFetch(queueItem("video-1", { state: "done" }));
    const proposal = metadataProposal("proposal-1");

    await applyIdentifyQueueItem("video-1", proposal, ["title", "images"], { poster: "https://img.test/poster.jpg" });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/identify/queue/entities/video-1/apply",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({
          proposal,
          selectedFields: ["title", "images"],
          selectedImages: { poster: "https://img.test/poster.jpg" },
        }),
      }),
    );
  });

  it("returns null for optional queue items that are not found", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("", { status: 404 })));

    await expect(fetchOptionalIdentifyQueueItem("missing")).resolves.toBeNull();
  });
});

function mockFetch(data: unknown) {
  const fetchMock = vi.fn(async () => new Response(JSON.stringify(data), {
    headers: { "Content-Type": "application/json" },
    status: 200,
  }));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function queueItem(id: string, options: { state?: string } = {}) {
  return {
    id: `queue-${id}`,
    entityId: id,
    entityKind: "video",
    title: "Friendship",
    isNsfw: false,
    state: options.state ?? "search",
    provider: "tmdb",
    action: "search",
    query: null,
    candidates: [],
    proposal: null,
    error: null,
    createdAt: "2026-05-27T00:00:00Z",
    updatedAt: "2026-05-27T00:00:00Z",
    completedAt: null,
  };
}

function metadataProposal(id: string) {
  return {
    proposalId: id,
    provider: "tmdb",
    targetKind: "video",
    confidence: 1,
    matchReason: null,
    patch: {
      title: "Friendship",
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
      flags: null,
    },
    images: [],
    children: [],
    relationships: [],
    candidates: [],
    targetEntityId: null,
  };
}
