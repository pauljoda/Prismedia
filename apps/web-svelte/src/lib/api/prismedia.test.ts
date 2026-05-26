import { afterEach, describe, expect, it, vi } from "vitest";
import {
  fetchEntities,
  updateEntityRating,
  updateEntityMetadata,
  uploadFiles,
  excludeFile,
  fileContentUrl,
  removeFileExclusion,
  type EntityMetadataUpdateRequest,
} from "./prismedia";

describe("api helpers", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("patches entity metadata through the kind-aware route when kind is known", async () => {
    const request: EntityMetadataUpdateRequest = {
      fields: ["title"],
      patch: {
        title: "New title",
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
    };
    const fetchMock = vi.fn(async () =>
      new Response(JSON.stringify({ id: "entity-1", kind: "video-series", title: "New title" }), {
        status: 200,
        headers: { "content-type": "application/json" },
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    await updateEntityMetadata("entity-1", request, { kind: "video-series" });

    expect(fetchMock).toHaveBeenCalledWith("/api/entities/video-series/entity-1", expect.objectContaining({
      method: "PATCH",
      body: JSON.stringify(request),
    }));
  });

  it("patches entity ratings through the same JSON API helper", async () => {
    const fetchMock = vi.fn(async () =>
      new Response(JSON.stringify({ id: "track-1", kind: "audio-track", title: "Track" }), {
        status: 200,
        headers: { "content-type": "application/json" },
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    await updateEntityRating("track-1", 4);

    expect(fetchMock).toHaveBeenCalledWith("/api/entities/track-1/rating", expect.objectContaining({
      method: "PATCH",
      body: JSON.stringify({ value: 4 }),
    }));
  });

  it("passes referenced entity and relationship code filters when listing entities", async () => {
    const fetchMock = vi.fn(async () =>
      new Response(JSON.stringify({ items: [], nextCursor: null, totalCount: 0 }), {
        status: 200,
        headers: { "content-type": "application/json" },
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    await fetchEntities({ referencedBy: "studio-1", relationshipCode: "studio", limit: 1000 });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/entities?referencedBy=studio-1&relationshipCode=studio&limit=1000",
      expect.objectContaining({ method: "GET" }),
    );
  });

  it("posts watched-root uploads as multipart data without a json content type", async () => {
    const fetchMock = vi.fn(async () =>
      new Response(JSON.stringify({ scansQueued: 1 }), {
        status: 200,
        headers: { "content-type": "application/json" },
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    await uploadFiles("root-1", "Incoming", [
      { file: new File(["clip"], "clip.mp4"), relativePath: "Season 1/clip.mp4" },
    ]);

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/api/files/upload");
    expect(init.method).toBe("POST");
    expect(init.body).toBeInstanceOf(FormData);
    expect(new Headers(init.headers).has("Content-Type")).toBe(false);

    const form = init.body as FormData;
    expect(form.get("rootId")).toBe("root-1");
    expect(form.get("targetPath")).toBe("Incoming");
    expect(form.get("relativePaths")).toBe("Season 1/clip.mp4");
    expect(form.get("files")).toBeInstanceOf(File);
  });

  it("builds encoded file content urls", () => {
    expect(fileContentUrl("root-1", "Season 1/clip 01.mp4")).toBe(
      "/api/files/content?rootId=root-1&path=Season+1%2Fclip+01.mp4",
    );
  });

  it("marks watched-root paths as excluded", async () => {
    const fetchMock = vi.fn(async () =>
      new Response(JSON.stringify({ scansQueued: 1 }), {
        status: 200,
        headers: { "content-type": "application/json" },
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    await excludeFile({ rootId: "root-1", path: "Skip/movie.mkv" });

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/api/files/exclusions");
    expect(init.method).toBe("POST");
    expect(init.body).toBe(JSON.stringify({ rootId: "root-1", path: "Skip/movie.mkv" }));
  });

  it("removes watched-root path exclusions", async () => {
    const fetchMock = vi.fn(async () =>
      new Response(JSON.stringify({ scansQueued: 1 }), {
        status: 200,
        headers: { "content-type": "application/json" },
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    await removeFileExclusion({ rootId: "root-1", path: "Skip/movie.mkv" });

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/api/files/exclusions?rootId=root-1&path=Skip%2Fmovie.mkv");
    expect(init.method).toBe("DELETE");
  });
});
