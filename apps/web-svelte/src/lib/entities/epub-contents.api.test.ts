import { describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({
  fetchBookContents: vi.fn(),
}));

vi.mock("$lib/api/books", () => ({
  fetchBookContents: mocks.fetchBookContents,
}));

import { loadEpubContents } from "./epub-contents";

describe("EPUB contents API hydration", () => {
  it("normalizes the compact server contract and resolves the current chapter by fraction", async () => {
    mocks.fetchBookContents.mockResolvedValue({
      items: [
        {
          id: "one.xhtml",
          title: "One",
          location: "one.xhtml",
          depth: "0",
          order: "0",
          sectionIndex: "0",
          startFraction: "0.1",
          endFraction: "0.4",
        },
        {
          id: "two.xhtml",
          title: "Two",
          location: "two.xhtml",
          depth: 0,
          order: 1,
          sectionIndex: null,
          startFraction: null,
          endFraction: null,
        },
        {
          id: "three.xhtml",
          title: "Three",
          location: "three.xhtml",
          depth: 0,
          order: 2,
          sectionIndex: 2,
          startFraction: 0.4,
          endFraction: 1,
        },
      ],
    });
    const controller = new AbortController();

    const result = await loadEpubContents(
      "book-1",
      "epubcfi(/6/14!/4/2)",
      controller.signal,
      0.65,
    );

    expect(mocks.fetchBookContents).toHaveBeenCalledWith("book-1", { signal: controller.signal });
    expect(result.entries[0]).toMatchObject({ depth: 0, sectionIndex: 0, startFraction: 0.1 });
    expect(result.entries[1]).toMatchObject({ sectionIndex: null, startFraction: null });
    expect(result.currentChapterId).toBe("three.xhtml");
  });
});
