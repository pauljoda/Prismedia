import { describe, expect, it } from "vitest";
import {
  addEpubChapterRanges,
  exactWebEpubResumeLocation,
  flattenEpubToc,
  resolveCurrentEpubChapter,
  resolveEpubChapterByFraction,
  type EpubBookNavigation,
  type EpubContentsEntry,
} from "./epub-contents";

describe("EPUB contents", () => {
  it("flattens nested navigable entries while preserving hierarchy and order", () => {
    const entries = flattenEpubToc([
      {
        label: "Part One",
        href: null,
        subitems: [
          { label: "Chapter One", href: "Text/chapter-1.xhtml", subitems: [] },
          { label: "Chapter Two", href: "Text/chapter-2.xhtml", subitems: [] },
        ],
      },
    ]);

    expect(entries).toEqual([
      {
        id: "Text/chapter-1.xhtml",
        title: "Chapter One",
        location: "Text/chapter-1.xhtml",
        depth: 1,
        order: 0,
        sectionIndex: null,
      },
      {
        id: "Text/chapter-2.xhtml",
        title: "Chapter Two",
        location: "Text/chapter-2.xhtml",
        depth: 1,
        order: 1,
        sectionIndex: null,
      },
    ]);
  });

  it("marks the nearest table-of-contents entry at or before the saved CFI section", () => {
    const navigation: EpubBookNavigation = {
      resolveHref: (href) => ({
        index: href.includes("one") ? 2 : href.includes("two") ? 5 : 8,
      }),
      resolveCFI: () => ({ index: 6 }),
    };
    const entries = flattenEpubToc([
      { label: "One", href: "one.xhtml", subitems: [] },
      { label: "Two", href: "two.xhtml", subitems: [] },
      { label: "Three", href: "three.xhtml", subitems: [] },
    ], navigation);

    expect(resolveCurrentEpubChapter(entries, "epubcfi(/6/14!/4/2)", navigation)?.id).toBe(
      "two.xhtml",
    );
  });

  it("keeps the deepest actionable label when parent and child share one location", () => {
    const entries = flattenEpubToc([
      {
        label: "Part One",
        href: "Text/chapter-1.xhtml",
        subitems: [
          { label: "Chapter One", href: "Text/chapter-1.xhtml", subitems: [] },
        ],
      },
    ]);

    expect(entries).toHaveLength(1);
    expect(entries[0]).toMatchObject({ title: "Chapter One", depth: 1, order: 0 });
  });

  it("adds global reading ranges from the EPUB section sizes", () => {
    const navigation: EpubBookNavigation = {
      resolveHref: (href) => ({ index: href.includes("one") ? 1 : 2 }),
      resolveCFI: () => null,
    };
    const entries = flattenEpubToc([
      { label: "One", href: "one.xhtml", subitems: [] },
      { label: "Two", href: "two.xhtml", subitems: [] },
    ], navigation);

    expect(addEpubChapterRanges(entries, [
      { size: 10 },
      { size: 20 },
      { size: 30 },
      { size: 40 },
    ])).toEqual([
      expect.objectContaining({ id: "one.xhtml", startFraction: 0.1, endFraction: 0.3 }),
      expect.objectContaining({ id: "two.xhtml", startFraction: 0.3, endFraction: 1 }),
    ]);
  });

  it("finds the current chapter from an estimated whole-book fraction", () => {
    const entries: EpubContentsEntry[] = [
      { id: "one", title: "One", location: "one", depth: 0, order: 0, sectionIndex: 0, startFraction: 0.1, endFraction: 0.3 },
      { id: "two", title: "Two", location: "two", depth: 0, order: 1, sectionIndex: 1, startFraction: 0.3, endFraction: 0.8 },
    ];

    expect(resolveEpubChapterByFraction(entries, 0.55)?.id).toBe("two");
  });

  it("treats a shared range boundary as the start of the later chapter", () => {
    const entries: EpubContentsEntry[] = [
      { id: "tyrion", title: "Tyrion", location: "tyrion", depth: 0, order: 0, sectionIndex: 0, startFraction: 0.2, endFraction: 0.3 },
      { id: "jon", title: "Jon", location: "jon", depth: 0, order: 1, sectionIndex: 1, startFraction: 0.3, endFraction: 0.4 },
    ];

    expect(resolveEpubChapterByFraction(entries, 0.3)?.id).toBe("jon");
  });

  it("uses canonical fraction instead of passing native EPUB locators to the web reader", () => {
    expect(exactWebEpubResumeLocation("epubcfi(/6/14!/4/2)")).toBe("epubcfi(/6/14!/4/2)");
    expect(exactWebEpubResumeLocation("Text/chapter.xhtml#prismedia-progress=0.75")).toBeNull();
    expect(exactWebEpubResumeLocation('{"href":"Text/chapter.xhtml","locations":{"progression":0.75}}')).toBeNull();
  });
});
