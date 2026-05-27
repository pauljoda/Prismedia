import { afterEach, describe, expect, it, vi } from "vitest";
import {
  excludeFile,
  fetchFileDetail,
  fileContentUrl,
  removeFileExclusion,
  uploadFiles,
} from "./files";

describe("files API", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("posts watched-root uploads as multipart data without a json content type", async () => {
    const fetchMock = mockFetch({ scansQueued: 1 });

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

  it("normalizes file detail directory counts", async () => {
    mockFetch({
      entry: {
        rootId: "root-1",
        path: "",
        name: "Movies",
        kind: "directory",
        sizeBytes: null,
        mimeType: null,
        modifiedAt: null,
      },
      absolutePath: "/media/movies",
      createdAt: null,
      linkedEntities: [],
      canPreview: false,
      directoryFileCount: "3",
      directoryTotalSizeBytes: "1024",
    });

    const detail = await fetchFileDetail("root-1");

    expect(detail.directoryFileCount).toBe(3);
    expect(detail.directoryTotalSizeBytes).toBe(1024);
  });

  it("marks watched-root paths as excluded", async () => {
    const fetchMock = mockFetch({ scansQueued: "1" });

    const response = await excludeFile({ rootId: "root-1", path: "Skip/movie.mkv" });

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/api/files/exclusions");
    expect(init.method).toBe("POST");
    expect(init.body).toBe(JSON.stringify({ rootId: "root-1", path: "Skip/movie.mkv" }));
    expect(response.scansQueued).toBe(1);
  });

  it("removes watched-root path exclusions", async () => {
    const fetchMock = mockFetch({ scansQueued: 1 });

    await removeFileExclusion({ rootId: "root-1", path: "Skip/movie.mkv" });

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/api/files/exclusions?rootId=root-1&path=Skip%2Fmovie.mkv");
    expect(init.method).toBe("DELETE");
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
