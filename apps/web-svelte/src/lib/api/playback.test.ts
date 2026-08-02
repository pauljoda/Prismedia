import { afterEach, describe, expect, it, vi } from "vitest";
import {
  createVideoPlaybackPlan,
  recordEntityPlaybackEvent,
  reportVideoPlayback,
  updateEntityPlayback,
  updateEntityProgress,
} from "./playback";

describe("playback API", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("creates video playback plans through the native playback route", async () => {
    const fetchMock = mockFetch({
      sessionId: "session-1",
      source: {
        id: "source-1",
        container: "mkv",
        durationSeconds: 60,
        method: "transcode",
        url: "/api/playback/videos/video-1/hls/master.m3u8",
        supportsTranscoding: true,
        streams: [],
        transcoding: null,
      },
    });

    const response = await createVideoPlaybackPlan("video-1", { enableTranscoding: true });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/playback/videos/video-1/plan",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ enableTranscoding: true }),
      }),
    );
    expect(response.sessionId).toBe("session-1");
  });

  it("updates entity progress through the generated route", async () => {
    const fetchMock = mockFetch(entityCard("book-1"));

    await updateEntityProgress("book-1", {
      currentEntityId: "chapter-1",
      unit: "page",
      index: 2,
      total: 10,
      completed: false,
      activitySeconds: 15,
      activityKind: "reading",
      utcOffsetMinutes: 0,
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
          activitySeconds: 15,
          activityKind: "reading",
          utcOffsetMinutes: 0,
        }),
      }),
    );
  });

  it("updates watched state through the entity playback route", async () => {
    const fetchMock = mockFetch(entityCard("video-1"));

    await updateEntityPlayback("video-1", {
      completed: true,
      resumeSeconds: 0,
      utcOffsetMinutes: 0,
    });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/entities/video-1/playback",
      expect.objectContaining({
        method: "PATCH",
        body: JSON.stringify({ completed: true, resumeSeconds: 0, utcOffsetMinutes: 0 }),
      }),
    );
  });

  it("reports playback progress through the native session route", async () => {
    const fetchMock = mockFetch(undefined, 204);

    await reportVideoPlayback("progress", {
      entityId: "video-1",
      sessionId: "session-1",
      positionSeconds: 12.5,
      utcOffsetMinutes: 0,
    });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/playback/sessions/progress",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({
          entityId: "video-1",
          sessionId: "session-1",
          positionSeconds: 12.5,
          durationSeconds: null,
          completed: null,
          activitySeconds: null,
          utcOffsetMinutes: 0,
        }),
      }),
    );
  });

  it("records explicit playback events through the entity event route", async () => {
    const fetchMock = mockFetch(entityCard("track-1"));

    await recordEntityPlaybackEvent("track-1", {
      kind: "skipped",
      positionSeconds: 4.2,
      durationSeconds: 180,
    });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/entities/track-1/playback/events",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({
          kind: "skipped",
          occurredAt: null,
          positionSeconds: 4.2,
          durationSeconds: 180,
          sessionId: null,
        }),
      }),
    );
  });
});

function mockFetch(data: unknown, status = 200) {
  const fetchMock = vi.fn(async () => new Response(
    data === undefined ? null : JSON.stringify(data),
    { headers: { "content-type": "application/json" }, status },
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
