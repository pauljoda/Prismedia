import { render, waitFor } from "@testing-library/svelte";
import { tick } from "svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { EntityCapability } from "$lib/api/generated/model";
import { fetchImage, fetchVideo } from "$lib/api/media";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
import EntityFeedHarness from "./EntityFeed.test-harness.svelte";

vi.mock("$lib/api/media", () => ({
  fetchImage: vi.fn(),
  fetchVideo: vi.fn(),
}));

const fetchImageMock = vi.mocked(fetchImage);
const fetchVideoMock = vi.mocked(fetchVideo);

describe("EntityFeed animated playback", () => {
  beforeEach(() => {
    fetchImageMock.mockReset();
    fetchVideoMock.mockReset();
    vi.stubGlobal("IntersectionObserver", PassiveIntersectionObserver);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("renders GIFs as animated images instead of video-capable clips", async () => {
    mockImages({
      "gif-1": imageDetail("gif-1", "Loop.gif", [
        filesCapability([{ role: "source", path: "/media/loop.gif", mimeType: "image/gif" }]),
        technicalCapability({ duration: "00:00:04", format: "gif" }),
      ]),
    });

    const { container } = render(EntityFeedHarness, {
      props: { cards: [card("gif-1", "Loop.gif")] },
    });

    await waitFor(() => {
      expect(container.querySelector<HTMLImageElement>(".feed-animated")?.getAttribute("src")).toBe(
        "/api/entities/gif-1/files/source",
      );
    });
    expect(container.querySelector("video.feed-video")).toBeNull();
  });

  it("uses the original source for the active image clip even when a preview is advertised", async () => {
    mockImages({
      "clip-1": imageDetail("clip-1", "Clip.webm", [
        filesCapability([
          { role: "source", path: "/media/clip.webm", mimeType: "video/webm" },
          { role: "preview", path: "/assets/images/clip-1/preview.mp4", mimeType: "video/mp4" },
        ]),
      ]),
    });

    const { container } = render(EntityFeedHarness, {
      props: { cards: [card("clip-1", "Clip.webm")] },
    });

    await waitFor(() => {
      expect(container.querySelector<HTMLVideoElement>("video.feed-video")?.getAttribute("src")).toBe(
        "/api/entities/clip-1/files/source",
      );
    });
  });

  it("hydrates video cards and mounts their feed playback source", async () => {
    mockVideos({
      "video-1": videoDetail("video-1", "Video.mp4", [
        filesCapability([
          { role: "preview", path: "/media/video-preview.mp4", mimeType: "video/mp4" },
          { role: "source", path: "/media/video.mp4", mimeType: "video/mp4" },
        ]),
        technicalCapability({ container: "mp4", codec: "h264" }),
      ]),
    });

    const { container } = render(EntityFeedHarness, {
      props: { cards: [card("video-1", "Video.mp4", "video")] },
    });

    await waitFor(() => {
      expect(fetchVideoMock).toHaveBeenCalledWith("video-1");
    });
    await waitFor(() => {
      expect(container.querySelector<HTMLVideoElement>("video.feed-video")?.getAttribute("src")).toBe(
        "/api/entities/video-1/files/source",
      );
    });
  });

  it("does not mount original-source video for nearby clips that have no preview", async () => {
    mockImages({
      "still-1": imageDetail("still-1", "Still.jpg", [
        filesCapability([{ role: "source", path: "/media/still.jpg", mimeType: "image/jpeg" }]),
      ]),
      "neighbor-1": imageDetail("neighbor-1", "Neighbor.webm", [
        filesCapability([{ role: "source", path: "/media/neighbor.webm", mimeType: "video/webm" }]),
      ]),
    });

    const { container } = render(EntityFeedHarness, {
      props: { cards: [card("still-1", "Still.jpg"), card("neighbor-1", "Neighbor.webm")] },
    });

    await waitFor(() => {
      expect(fetchImageMock).toHaveBeenCalledWith("neighbor-1");
    });
    await tick();

    expect(container.querySelector("video.feed-video")).toBeNull();
  });
});

class PassiveIntersectionObserver implements IntersectionObserver {
  readonly root = null;
  readonly rootMargin = "";
  readonly thresholds = [];

  disconnect(): void {}
  observe(_target: Element): void {}
  takeRecords(): IntersectionObserverEntry[] {
    return [];
  }
  unobserve(_target: Element): void {}
}

type FetchImageResult = Awaited<ReturnType<typeof fetchImage>>;
type FetchVideoResult = Awaited<ReturnType<typeof fetchVideo>>;

function mockImages(images: Record<string, FetchImageResult>): void {
  fetchImageMock.mockImplementation(async (id: string) => {
    const image = images[id];
    if (!image) throw new Error(`Unexpected image request: ${id}`);
    return image;
  });
}

function mockVideos(videos: Record<string, FetchVideoResult>): void {
  fetchVideoMock.mockImplementation(async (id: string) => {
    const video = videos[id];
    if (!video) throw new Error(`Unexpected video request: ${id}`);
    return video;
  });
}

function imageDetail(id: string, title: string, capabilities: EntityCapability[]): FetchImageResult {
  return {
    id,
    kind: "image",
    title,
    capabilities,
  } as FetchImageResult;
}

function videoDetail(id: string, title: string, capabilities: EntityCapability[]): FetchVideoResult {
  return {
    id,
    kind: "video",
    title,
    capabilities,
  } as FetchVideoResult;
}

function filesCapability(items: Array<{ role: string; path: string; mimeType: string }>): EntityCapability {
  return {
    kind: "files",
    items,
  } as EntityCapability;
}

function technicalCapability(overrides: Partial<Record<string, string | number | null>> = {}): EntityCapability {
  return {
    kind: "technical",
    duration: null,
    width: 640,
    height: 360,
    frameRate: null,
    bitRate: null,
    sampleRate: null,
    channels: null,
    codec: null,
    container: null,
    format: null,
    ...overrides,
  } as EntityCapability;
}

function card(id: string, title: string, kind = "image"): EntityThumbnailCard {
  return {
    entity: {
      id,
      kind,
      title,
      parentEntityId: null,
      sortOrder: null,
      relationships: [],
      capabilities: [],
      childrenByKind: [],
    },
    aspectRatio: "square",
    cover: {
      src: `/covers/${id}.jpg`,
      alt: title,
    },
    hover: { kind: "none" },
    fit: "cover",
    meta: [],
  };
}
