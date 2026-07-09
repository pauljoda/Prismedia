import { describe, expect, it, vi } from "vitest";
import {
  applyIdentifyQueueItem,
  fetchIdentifyEntities,
  fetchIdentifyQueue,
  fetchOptionalIdentifyQueueItem,
  requestIdentifySearch,
  resolveIdentifyQueueCandidate,
  startBulkIdentify,
} from "./identify-client";

describe("identify client", () => {
  it("lists only acquired Entities backed by local files", async () => {
    const fetchMock = mockFetch({ items: [], nextCursor: null });

    await fetchIdentifyEntities("video-series");

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/entities?kind=video-series&hasFile=true&wanted=false",
      expect.objectContaining({ method: "GET" }),
    );
  });

  it("passes NSFW visibility through the generated queue params", async () => {
    const fetchMock = mockFetch([]);

    await fetchIdentifyQueue(false, true);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/identify/queue?includeCompleted=false&hideNsfw=true",
      expect.objectContaining({ method: "GET" }),
    );
  });

  it("posts search requests through the generated endpoint", async () => {
    const fetchMock = mockFetch(queueItem("video-1"));

    await requestIdentifySearch("video-1", "tmdb", { title: "Friendship" });

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/api/identify/queue/entities/video-1/search"),
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ provider: "tmdb", query: { title: "Friendship" } }),
      }),
    );
  });

  it("posts selected candidates through the direct queue resolve endpoint", async () => {
    const fetchMock = mockFetch(queueItem("video-1", { state: "proposal" }));
    const candidate = searchCandidate();

    await resolveIdentifyQueueCandidate("video-1", "tmdb", candidate, true);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/identify/queue/entities/video-1/candidate?hideNsfw=true",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ provider: "tmdb", candidate }),
      }),
    );
  });

  it("uses API problem messages for selected candidate resolve errors", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(
      JSON.stringify({ code: "identify_failed", message: "No TMDB match was found." }),
      { headers: { "Content-Type": "application/json" }, status: 400 },
    )));

    await expect(resolveIdentifyQueueCandidate("video-1", "tmdb", searchCandidate()))
      .rejects.toThrow("No TMDB match was found.");
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

  it("starts durable bulk identify jobs through the generated endpoint", async () => {
    const fetchMock = mockFetch(jobCreateResponse("job-1"), 202);

    await startBulkIdentify("tmdb", ["video-1", "video-2"], null, true);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/identify/bulk?hideNsfw=true",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ provider: "tmdb", entityIds: ["video-1", "video-2"], query: null }),
      }),
    );
  });

  it("returns null for optional queue items that are not found", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("", { status: 404 })));

    await expect(fetchOptionalIdentifyQueueItem("missing")).resolves.toBeNull();
  });
});

function mockFetch(data: unknown, status = 200) {
  const fetchMock = vi.fn(async () => new Response(JSON.stringify(data), {
    headers: { "Content-Type": "application/json" },
    status,
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

function searchCandidate() {
  return {
    externalIds: { tmdb: "2005" },
    title: "Ambiguous Movie",
    year: 2005,
    overview: "Candidate overview.",
    posterUrl: "https://image.test/poster.jpg",
    popularity: 9.1,
  };
}

function jobCreateResponse(id: string) {
  return {
    job: {
      id,
      type: "bulk-identify",
      status: "queued",
      progress: 0,
      message: "Queued",
      targetKind: null,
      targetId: null,
      targetLabel: "Bulk identify 2 entities",
      createdAt: "2026-05-27T00:00:00Z",
      startedAt: null,
      finishedAt: null,
    },
  };
}

function metadataProposal(id: string) {
  return {
    proposalId: id,
    provider: "tmdb",
    targetKind: "video" as const,
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
