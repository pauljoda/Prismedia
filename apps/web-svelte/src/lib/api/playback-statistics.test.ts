import { afterEach, describe, expect, it, vi } from "vitest";
import { fetchPlaybackStatistics } from "./playback-statistics";

describe("playback statistics API", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("serializes individual-user and all-user scopes", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify(emptyStatistics()), {
      headers: { "content-type": "application/json" },
      status: 200,
    }));
    vi.stubGlobal("fetch", fetchMock);

    await fetchPlaybackStatistics({ userId: "user-1" });
    await fetchPlaybackStatistics({ allUsers: true });

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      "/api/playback/statistics?userId=user-1",
      expect.any(Object),
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      "/api/playback/statistics?allUsers=true",
      expect.any(Object),
    );
  });
});

function emptyStatistics() {
  return {
    from: "2026-01-01T00:00:00Z",
    to: "2026-01-02T00:00:00Z",
    totalEvents: 0,
    completedCount: 0,
    skippedCount: 0,
    distinctEntityCount: 0,
    topEntities: [],
    recentEvents: [],
    dailyEvents: [],
  };
}
