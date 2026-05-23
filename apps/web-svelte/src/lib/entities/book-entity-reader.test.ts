import { describe, expect, it } from "vitest";
import type { EntityCard, EntityThumbnail } from "$lib/api/generated/model";
import {
  bookEntityProgressDisplay,
  entityPageToReaderImage,
  orderedBookChildren,
} from "./book-entity-reader";

function thumbnail(overrides: Partial<EntityThumbnail>): EntityThumbnail {
  return {
    id: "entity",
    kind: "book-page",
    title: "Page",
    parentEntityId: null,
    sortOrder: null,
    coverUrl: null,
    hoverKind: "none",
    hoverUrl: null,
    hoverImages: [],
    meta: [],
    rating: null,
    isFavorite: false,
    isNsfw: false,
    isOrganized: true,
    ...overrides,
  };
}

function entity(overrides: Partial<EntityCard>): EntityCard {
  return {
    id: "book-1",
    kind: "book",
    title: "Book",
    parentEntityId: null,
    sortOrder: null,
    capabilities: [],
    childrenByKind: [],
    relationships: [],
    ...overrides,
  };
}

describe("book entity reader helpers", () => {
  it("orders child thumbnails by structural sort order", () => {
    const source = entity({
      childrenByKind: [
        {
          kind: "book-chapter",
          label: "Chapters",
          entities: [
            thumbnail({ id: "chapter-2", kind: "book-chapter", title: "Second", sortOrder: 2 }),
            thumbnail({ id: "chapter-1", kind: "book-chapter", title: "First", sortOrder: 1 }),
          ],
        },
      ],
    });

    expect(orderedBookChildren(source, "book-chapter").map((item) => item.id)).toEqual([
      "chapter-1",
      "chapter-2",
    ]);
  });

  it("maps page entities to the existing reader image contract", () => {
    const image = entityPageToReaderImage(
      thumbnail({
        id: "page-1",
        title: "Page 1",
        sortOrder: 3,
        coverUrl: "/assets/book-pages/page-1/thumb.jpg",
        isNsfw: true,
      }),
    );

    expect(image).toMatchObject({
      id: "page-1",
      title: "Page 1",
      isNsfw: true,
      thumbnailPath: "/assets/book-pages/page-1/thumb.jpg",
      fullPath: "/entities/page-1/files/source",
      sortOrder: 3,
    });
  });

  it("describes current book progress from the shared progress capability", () => {
    const display = bookEntityProgressDisplay(
      entity({
        capabilities: [
          {
            kind: "progress",
            currentEntityId: "chapter-2",
            unit: "page",
            index: 4,
            total: 24,
            mode: "webtoon",
            completedAt: null,
            updatedAt: "2026-05-22T12:00:00.000Z",
          },
        ],
      }),
      [{ id: "chapter-2", title: "The Brass Door", sortOrder: 1, pageCount: 24 }],
    );

    expect(display).toMatchObject({
      chapterId: "chapter-2",
      chapterLabel: "Ch. 2: The Brass Door",
      pageLabel: "Page 5 of 24",
      chapterPageLabel: "Chapter page 5 of 24",
      workPageLabel: "Book page 5 of 24",
      percent: 21,
      isComplete: false,
    });
  });

  it("uses whole-work progress when the API provides book-level position fields", () => {
    const display = bookEntityProgressDisplay(
      entity({
        capabilities: [
          {
            kind: "progress",
            currentEntityId: "chapter-2",
            unit: "page",
            index: 4,
            total: 24,
            mode: "paged",
            completedAt: null,
            updatedAt: "2026-05-22T12:00:00.000Z",
            workIndex: 104,
            workTotal: 240,
          },
        ],
      }),
      [{ id: "chapter-2", title: "The Brass Door", sortOrder: 1, pageCount: 24 }],
    );

    expect(display).toMatchObject({
      pageLabel: "Page 5 of 24",
      chapterPageLabel: "Chapter page 5 of 24",
      workPageLabel: "Book page 105 of 240",
      workPage: 105,
      workTotal: 240,
      percent: 44,
    });
  });
});
