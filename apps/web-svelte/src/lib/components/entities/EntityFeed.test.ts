import { render, waitFor } from "@testing-library/svelte";
import { tick } from "svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { EntityCapability } from "$lib/api/generated/model";
import { fetchImage } from "$lib/api/media";
import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
import EntityFeedHarness from "./EntityFeed.test-harness.svelte";

vi.mock("$lib/api/media", () => ({
  fetchImage: vi.fn(),
}));

const fetchImageMock = vi.mocked(fetchImage);

describe("EntityFeed animated playback", () => {
  beforeEach(() => {
    fetchImageMock.mockReset();
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

  it("falls back to the original source for the active clip when no preview exists", async () => {
    mockImages({
      "clip-1": imageDetail("clip-1", "Clip.webm", [
        filesCapability([{ role: "source", path: "/media/clip.webm", mimeType: "video/webm" }]),
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

function mockImages(images: Record<string, FetchImageResult>): void {
  fetchImageMock.mockImplementation(async (id: string) => {
    const image = images[id];
    if (!image) throw new Error(`Unexpected image request: ${id}`);
    return image;
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

function card(id: string, title: string): EntityThumbnailCard {
  return {
    entity: {
      id,
      kind: "image",
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
