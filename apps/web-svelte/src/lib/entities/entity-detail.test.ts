import { describe, expect, it } from "vitest";
import type { EntityCard } from "$lib/api/generated/model";
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
});
