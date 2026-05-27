import { describe, expect, it, vi } from "vitest";
import {
  clearEntityImageAsset,
  updateEntityFlags,
  updateEntityMetadata,
  updateEntityRating,
  uploadEntityImageAsset,
} from "./entity-mutations";

describe("entity mutation API", () => {
  it("updates ratings through the generated entity endpoint", async () => {
    const fetchMock = mockFetch({ id: "entity-1" });

    await updateEntityRating("entity-1", 4);

    expect(fetchMock).toHaveBeenCalledWith("/api/entities/entity-1/rating", expect.objectContaining({
      body: JSON.stringify({ value: 4 }),
      method: "PATCH",
    }));
  });

  it("updates scoped metadata through the generated kind endpoint", async () => {
    const fetchMock = mockFetch({ id: "entity-1" });
    const request = {
      fields: ["title"],
      patch: {
        title: "New title",
        externalIds: {},
        urls: [],
        tags: [],
        credits: [],
        dates: {},
        stats: {},
        positions: {},
      },
    };

    await updateEntityMetadata("entity-1", request, { kind: "video-series" });

    expect(fetchMock).toHaveBeenCalledWith("/api/entities/video-series/entity-1", expect.objectContaining({
      body: JSON.stringify(request),
      method: "PATCH",
    }));
  });

  it("normalizes partial flag updates for the generated endpoint", async () => {
    const fetchMock = mockFetch({ id: "entity-1" });

    await updateEntityFlags("entity-1", { isFavorite: true });

    expect(fetchMock).toHaveBeenCalledWith("/api/entities/entity-1/flags", expect.objectContaining({
      body: JSON.stringify({ isFavorite: true, isNsfw: null, isOrganized: null }),
      method: "PATCH",
    }));
  });

  it("keeps image upload and clear helpers on multipart-compatible fetch paths", async () => {
    const fetchMock = mockFetch({ id: "entity-1" });
    const file = new File(["cover"], "cover.jpg", { type: "image/jpeg" });

    await uploadEntityImageAsset("entity-1", "poster", file);
    await clearEntityImageAsset("entity-1", "poster");

    expect(fetchMock).toHaveBeenNthCalledWith(1, "/api/entities/entity-1/images/poster", expect.objectContaining({
      body: expect.any(FormData),
      method: "POST",
    }));
    expect(fetchMock).toHaveBeenNthCalledWith(2, "/api/entities/entity-1/images/poster", expect.objectContaining({
      method: "DELETE",
    }));
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
