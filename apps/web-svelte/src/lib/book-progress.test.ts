import { describe, expect, it } from "vitest";
import { getChapterProgressDisplay, getCurrentChapterProgressDisplay } from "./book-progress";

const chapters = [
  { id: "chapter-1", title: "Opening", chapterNumber: 1, pageCount: 12 },
  { id: "chapter-2", title: "The Brass Door", chapterNumber: 2, pageCount: 24 },
];

describe("book progress display", () => {
  it("describes the current chapter and page for the book root", () => {
    const display = getCurrentChapterProgressDisplay({
      chapters,
      progress: {
        bookId: "book-1",
        chapterId: "chapter-2",
        pageIndex: 4,
        pageCount: 24,
        readerMode: "paged",
        completedAt: null,
        updatedAt: "2026-05-11T12:00:00.000Z",
      },
    });

    expect(display?.summaryLabel).toBe("Reading Ch. 2: The Brass Door - Page 5 of 24");
    expect(display?.percent).toBe(21);
  });

  it("shows completed chapter progress in the chapter detail view", () => {
    const display = getChapterProgressDisplay(
      {
        chapters,
        progress: {
          bookId: "book-1",
          chapterId: "chapter-2",
          pageIndex: 23,
          pageCount: 24,
          readerMode: "webtoon",
          completedAt: "2026-05-11T12:00:00.000Z",
          updatedAt: "2026-05-11T12:00:00.000Z",
        },
      },
      chapters[1],
    );

    expect(display?.detailLabel).toBe("Read");
    expect(display?.pageLabel).toBeNull();
    expect(display?.percent).toBe(100);
    expect(display?.showMeter).toBe(false);
  });

  it("does not expose progress on a chapter detail page for another chapter", () => {
    const display = getChapterProgressDisplay(
      {
        chapters,
        progress: {
          bookId: "book-1",
          chapterId: "chapter-2",
          pageIndex: 4,
          pageCount: 24,
          readerMode: "paged",
          completedAt: null,
          updatedAt: "2026-05-11T12:00:00.000Z",
        },
      },
      chapters[0],
    );

    expect(display).toBeNull();
  });
});
