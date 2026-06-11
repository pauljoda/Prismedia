import { describe, expect, it } from "vitest";
import type { EntityCard, EntityKind } from "$lib/api/generated/model";
import { entityCardToDetailCard } from "./entity-detail";

describe("entity detail view model", () => {
  it("uses backdrop artwork for the hero while keeping poster artwork for the poster slot", () => {
    const detail = entityCardToDetailCard({
      id: "series-1",
      kind: "video-series",
      title: "The Chair Company",
      parentEntityId: null,
      sortOrder: null,
      capabilities: [
        {
          kind: "images",
          supportedKinds: ["poster", "backdrop", "logo"],
          thumbnailUrl: null,
          coverUrl: "/assets/series/series-1/poster.jpg",
          items: [
            { kind: "poster", path: "/assets/series/series-1/poster.jpg", mimeType: "image/jpeg" },
            { kind: "backdrop", path: "/assets/series/series-1/backdrop.jpg", mimeType: "image/jpeg" },
            { kind: "logo", path: "/assets/series/series-1/logo.png", mimeType: "image/png" },
          ],
        },
      ],
      childrenByKind: [],
      relationships: [],
    } satisfies EntityCard);

    expect(detail.hero?.src).toBe("/assets/series/series-1/backdrop.jpg");
    expect(detail.poster?.src).toBe("/assets/series/series-1/poster.jpg");
  });

  it("does not promote poster cover URLs into header artwork", () => {
    const detail = entityCardToDetailCard({
      id: "season-1",
      kind: "video-season",
      title: "Season 1",
      parentEntityId: "series-1",
      sortOrder: null,
      capabilities: [
        {
          kind: "images",
          supportedKinds: ["poster"],
          thumbnailUrl: null,
          coverUrl: "/assets/seasons/season-1/poster.jpg",
          items: [
            { kind: "poster", path: "/assets/seasons/season-1/poster.jpg", mimeType: "image/jpeg" },
          ],
        },
      ],
      childrenByKind: [],
      relationships: [],
    } satisfies EntityCard);

    expect(detail.hero).toBeNull();
    expect(detail.poster?.src).toBe("/assets/seasons/season-1/poster.jpg");
  });

  it("uses cover URLs as poster artwork when no poster item exists", () => {
    const detail = entityCardToDetailCard({
      id: "book-1",
      kind: "book",
      title: "Book",
      parentEntityId: null,
      sortOrder: null,
      capabilities: [
        {
          kind: "images",
          supportedKinds: ["cover"],
          thumbnailUrl: null,
          coverUrl: "/assets/books/book-1/cover.jpg",
          items: [],
        },
      ],
      childrenByKind: [],
      relationships: [],
    } satisfies EntityCard);

    expect(detail.hero).toBeNull();
    expect(detail.poster?.src).toBe("/assets/books/book-1/cover.jpg");
  });

  it("uses logo artwork as poster fallback for entities without poster art", () => {
    const detail = entityCardToDetailCard({
      id: "studio-1",
      kind: "studio",
      title: "HBO",
      parentEntityId: null,
      sortOrder: null,
      capabilities: [
        {
          kind: "images",
          supportedKinds: ["logo"],
          thumbnailUrl: null,
          coverUrl: null,
          items: [{ kind: "logo", path: "/assets/studios/hbo-logo.png", mimeType: "image/png" }],
        },
      ],
      childrenByKind: [],
      relationships: [],
    } satisfies EntityCard);

    expect(detail.poster?.src).toBe("/assets/studios/hbo-logo.png");
  });

  it("builds detail poster previews from every child group", () => {
    const detail = entityCardToDetailCard({
      id: "gallery-1",
      kind: "gallery",
      title: "Gallery",
      parentEntityId: null,
      sortOrder: null,
      capabilities: [
        {
          kind: "images",
          supportedKinds: ["thumbnail"],
          thumbnailUrl: "/assets/gallery/cover.jpg",
          coverUrl: "/assets/gallery/cover.jpg",
          items: [
            { kind: "thumbnail", path: "/assets/gallery/cover.jpg", mimeType: "image/jpeg" },
          ],
        },
      ],
      childrenByKind: [
        {
          kind: "gallery",
          label: "Galleries",
          entities: [
            thumbnail("subgallery-1", "gallery", "Sub Gallery", "/assets/gallery/sub.jpg"),
          ],
        },
        {
          kind: "image",
          label: "Images",
          entities: [
            thumbnail("image-1", "image", "Image 1", "/assets/gallery/image-1.jpg"),
            thumbnail("image-2", "image", "Image 2", "/assets/gallery/image-2.jpg"),
            thumbnail("image-3", "image", "Image 3", "/assets/gallery/image-3.jpg"),
          ],
        },
      ],
      relationships: [],
    } satisfies EntityCard);

    expect(detail.posterCard?.hover.kind).toBe("image-sequence");
    if (detail.posterCard?.hover.kind === "image-sequence") {
      expect(detail.posterCard.hover.assets.map((asset) => asset.src)).toEqual([
        "/assets/gallery/sub.jpg",
        "/assets/gallery/image-1.jpg",
        "/assets/gallery/image-2.jpg",
        "/assets/gallery/image-3.jpg",
      ]);
    }
  });

  it("uses the shared thumbnail hover model for comic detail posters", () => {
    const detail = entityCardToDetailCard({
      id: "comic-1",
      kind: "book",
      title: "Comic",
      parentEntityId: null,
      sortOrder: null,
      capabilities: [
        {
          kind: "images",
          supportedKinds: ["cover"],
          thumbnailUrl: "/assets/comics/comic-1/cover.jpg",
          coverUrl: "/assets/comics/comic-1/cover.jpg",
          items: [
            { kind: "cover", path: "/assets/comics/comic-1/cover.jpg", mimeType: "image/jpeg" },
          ],
        },
      ],
      childrenByKind: [
        {
          kind: "book-page",
          label: "Pages",
          entities: [
            thumbnail("page-1", "book-page", "Page 1", "/assets/comics/comic-1/page-1.jpg", "comic-1"),
            thumbnail("page-2", "book-page", "Page 2", "/assets/comics/comic-1/page-2.jpg", "comic-1"),
          ],
        },
      ],
      relationships: [],
    } satisfies EntityCard);

    expect(detail.posterCard?.hover.kind).toBe("image-sequence");
    if (detail.posterCard?.hover.kind === "image-sequence") {
      expect(detail.posterCard.hover.assets.map((asset) => asset.src)).toEqual([
        "/assets/comics/comic-1/page-1.jpg",
        "/assets/comics/comic-1/page-2.jpg",
      ]);
    }
  });

  it("does not promote bitrate into detail metadata", () => {
    const detail = entityCardToDetailCard({
      id: "video-1",
      kind: "video",
      title: "Episode",
      parentEntityId: null,
      sortOrder: null,
      capabilities: [
        {
          kind: "technical",
          duration: "00:24:10",
          width: 1920,
          height: 1080,
          frameRate: null,
          bitRate: 10_200_000,
          sampleRate: null,
          channels: null,
          codec: "h264",
          container: "mkv",
          format: null,
        },
      ],
      childrenByKind: [],
      relationships: [],
    } satisfies EntityCard);

    expect(detail.technical.map((row) => row.label)).toEqual([
      "Duration",
      "Resolution",
      "Codec",
      "Container",
    ]);
    expect(detail.technical.map((row) => row.value)).not.toContain("10.2 Mbps");
  });

  it("does not expose provider popularity as a generic detail stat", () => {
    const detail = entityCardToDetailCard({
      id: "person-1",
      kind: "person",
      title: "Lead Actor",
      parentEntityId: null,
      sortOrder: null,
      capabilities: [
        {
          kind: "stats",
          items: [
            { code: "popularity", value: 12 },
            { code: "credits", value: 3 },
          ],
        },
      ],
      childrenByKind: [],
      relationships: [],
    } satisfies EntityCard);

    expect(detail.stats.map((row) => row.code)).toEqual(["credits"]);
  });

  it("displays progress indexes as one-based positions", () => {
    const detail = entityCardToDetailCard({
      id: "book-1",
      kind: "book",
      title: "Book",
      parentEntityId: null,
      sortOrder: null,
      capabilities: [
        {
          kind: "progress",
          currentEntityId: "chapter-1",
          unit: "page",
          index: 24,
          total: 25,
          mode: "paged",
          completedAt: null,
          updatedAt: "2026-05-30T12:00:00Z",
        },
      ],
      childrenByKind: [],
      relationships: [],
    } satisfies EntityCard);

    expect(detail.progress).toMatchObject({
      index: 25,
      total: 25,
      percent: 100,
      completed: false,
    });
  });

  it("labels plugin person classification by meaning instead of source", () => {
    const detail = entityCardToDetailCard({
      id: "person-1",
      kind: "person",
      title: "Lead Actor",
      parentEntityId: null,
      sortOrder: null,
      capabilities: [
        {
          kind: "classification",
          value: "Acting",
          system: "plugin",
        },
      ],
      childrenByKind: [],
      relationships: [],
    } satisfies EntityCard);

    expect(detail.classification).toMatchObject({
      label: "Known For",
      value: "Acting",
      system: "plugin",
    });
  });
});

function thumbnail(id: string, kind: EntityKind, title: string, coverUrl: string, parentEntityId = "gallery-1") {
  return {
    id,
    kind,
    title,
    parentEntityId,
    sortOrder: null,
    coverUrl,
    coverThumbUrl: null,
    hoverKind: "none" as const,
    hoverUrl: null,
    hoverImages: [],
    meta: [],
    rating: null,
    isFavorite: false,
    isNsfw: false,
    isOrganized: false,
  };
}
