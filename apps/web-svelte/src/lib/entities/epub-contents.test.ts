import { describe, expect, it } from "vitest";
import {
  flattenEpubToc,
  resolveCurrentEpubChapter,
  type EpubBookNavigation,
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
});
