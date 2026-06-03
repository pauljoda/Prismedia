import { describe, expect, it } from "vitest";
import type { EntityCard, EntityThumbnail } from "$lib/api/generated/model";
import {
  bookEntityProgressDisplay,
  entityPageToReaderImage,
  orderedBookChildren,
  singleFileBookProgressDisplay,
} from "./book-entity-reader";

function thumbnail(overrides: Partial<EntityThumbnail>): EntityThumbnail {
  return {
    id: "entity",
    kind: "book-page",
    title: "Page",
    parentEntityId: null,
    sortOrder: null,
    coverUrl: null,
    coverThumbUrl: null,
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

  it("treats the last zero-based page index as complete for display", () => {
    const display = bookEntityProgressDisplay(
      entity({
        capabilities: [
          {
            kind: "progress",
            currentEntityId: "chapter-1",
            unit: "page",
            index: 24,
            total: 25,
            mode: "paged",
            completedAt: null,
            updatedAt: "2026-05-30T12:00:00.000Z",
            workIndex: 24,
            workTotal: 25,
          },
        ],
      }),
      [{ id: "chapter-1", title: "Finale", sortOrder: 0, pageCount: 25 }],
    );

    expect(display).toMatchObject({
      currentPage: 25,
      pageCount: 25,
      workPage: 25,
      workTotal: 25,
      percent: 100,
      isComplete: true,
      showMeter: false,
      pageLabel: null,
      detailLabel: "Read",
    });
  });

  it("returns no single-file progress before a book has been opened", () => {
    expect(singleFileBookProgressDisplay(entity({ capabilities: [] }))).toBeNull();
    expect(
      singleFileBookProgressDisplay(
        entity({
          capabilities: [
            {
              kind: "progress",
              currentEntityId: null,
              unit: "page",
              index: 0,
              total: 0,
              mode: null,
              completedAt: null,
              updatedAt: null,
            },
          ],
        }),
      ),
    ).toBeNull();
  });

  it("describes single-file PDF progress with a page position", () => {
    const display = singleFileBookProgressDisplay(
      entity({
        capabilities: [
          {
            kind: "progress",
            currentEntityId: "book-1",
            unit: "page",
            index: 44,
            total: 200,
            mode: "scrolled",
            completedAt: null,
            updatedAt: "2026-05-22T12:00:00.000Z",
          },
        ],
      }),
    );

    expect(display).toMatchObject({
      percent: 23,
      isComplete: false,
      positionLabel: "Page 45 of 200",
      unit: "page",
      index: 44,
      total: 200,
      mode: "scrolled",
    });
  });

  it("describes single-file EPUB progress as a percentage of the reading fraction", () => {
    const display = singleFileBookProgressDisplay(
      entity({
        capabilities: [
          {
            kind: "progress",
            currentEntityId: "book-1",
            unit: "cfi",
            index: 3300,
            total: 10000,
            mode: "paged",
            completedAt: null,
            updatedAt: "2026-05-22T12:00:00.000Z",
            location: "epubcfi(/6/14!/4/2)",
          },
        ],
      }),
    );

    expect(display).toMatchObject({
      percent: 33,
      isComplete: false,
      positionLabel: "33% read",
      unit: "cfi",
      location: "epubcfi(/6/14!/4/2)",
    });
  });

  it("treats a completed single-file book as fully read with no position label", () => {
    const display = singleFileBookProgressDisplay(
      entity({
        capabilities: [
          {
            kind: "progress",
            currentEntityId: "book-1",
            unit: "cfi",
            index: 10000,
            total: 10000,
            mode: "paged",
            completedAt: "2026-05-30T12:00:00.000Z",
            updatedAt: "2026-05-30T12:00:00.000Z",
          },
        ],
      }),
    );

    expect(display).toMatchObject({
      percent: 100,
      isComplete: true,
      positionLabel: null,
    });
  });
});
