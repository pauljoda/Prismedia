import { afterEach, describe, expect, it, vi } from "vitest";
import {
  fetchJellyfinPlaybackInfo,
  markJellyfinUserPlayedItem,
  postJellyfinSessionProgress,
  updateEntityProgress,
} from "./playback";

describe("playback API", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("posts Jellyfin playback info requests to the root route", async () => {
    const fetchMock = mockFetch({ PlaySessionId: "session-1", MediaSources: [] });

    const response = await fetchJellyfinPlaybackInfo("video-1", { EnableTranscoding: true });

    expect(fetchMock).toHaveBeenCalledWith(
      "/Items/video-1/PlaybackInfo",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ EnableTranscoding: true }),
      }),
    );
    expect(response.PlaySessionId).toBe("session-1");
  });

  it("updates entity progress through the generated route", async () => {
    const fetchMock = mockFetch(entityCard("book-1"));

    await updateEntityProgress("book-1", {
      currentEntityId: "chapter-1",
      unit: "page",
      index: 2,
      total: 10,
      completed: false,
    });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/entities/book-1/progress",
      expect.objectContaining({
        method: "PATCH",
        body: JSON.stringify({
          currentEntityId: "chapter-1",
          unit: "page",
          index: 2,
          total: 10,
          mode: null,
          completed: false,
          reset: false,
          location: null,
        }),
      }),
    );
  });

  it("marks Jellyfin items played through the root (non-/api) route", async () => {
    const fetchMock = mockFetch(undefined);

    await markJellyfinUserPlayedItem("video-1", true);

    expect(fetchMock).toHaveBeenCalledWith(
      "/UserPlayedItems/video-1",
      expect.objectContaining({ method: "POST" }),
    );
  });

  it("posts Jellyfin session progress to the root (non-/api) route", async () => {
    const fetchMock = mockFetch(undefined);

    await postJellyfinSessionProgress("Playing/Progress", { ItemId: "video-1", PositionTicks: 100 });

    expect(fetchMock).toHaveBeenCalledWith(
      "/Sessions/Playing/Progress",
      expect.objectContaining({ method: "POST" }),
    );
  });
});

function mockFetch(data: unknown) {
  const fetchMock = vi.fn(async () => new Response(
    data === undefined ? "" : JSON.stringify(data),
    { headers: { "content-type": "application/json" }, status: 200 },
  ));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function entityCard(id: string) {
  return {
    id,
    kind: "book",
    title: id,
    capabilities: [],
    groups: [],
  };
}
