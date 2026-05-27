import { afterEach, describe, expect, it, vi } from "vitest";
import { fetchBook, fetchSeason, fetchVideo } from "./media";

describe("media API", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("fetches video details through the generated route", async () => {
    const fetchMock = mockFetch(entityDetail("video-1", "video"));

    const video = await fetchVideo("video-1");

    expect(fetchMock).toHaveBeenCalledWith("/api/videos/video-1", expect.anything());
    expect(video.id).toBe("video-1");
  });

  it("fetches seasons with the parent series id", async () => {
    const fetchMock = mockFetch(entityDetail("season-1", "video-season"));

    await fetchSeason("series-1", "season-1");

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/series/series-1/seasons/season-1",
      expect.anything(),
    );
  });

  it("throws generated route errors with context", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("missing", { status: 404 })));

    await expect(fetchBook("book-404")).rejects.toThrow("missing");
  });
});

function mockFetch(data: unknown) {
  const fetchMock = vi.fn(async () => new Response(JSON.stringify(data), {
    headers: { "content-type": "application/json" },
    status: 200,
  }));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function entityDetail(id: string, kind: string) {
  return {
    id,
    kind,
    title: id,
    capabilities: [],
    groups: [],
  };
}
